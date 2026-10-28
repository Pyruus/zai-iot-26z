using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zai.Api.Auth;
using Zai.Api.Contracts;
using Zai.Api.Data;
using Zai.Api.Domain;
using Zai.Api.Infrastructure;

namespace Zai.Api.Controllers;

[ApiController]
[Route("api/measurements")]
public sealed class MeasurementsController(AppDbContext db, TimeProvider clock, EventLogger events) : ControllerBase
{
    private const int DefaultLimit = 1000;
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? series,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? sort,
        [FromQuery, Range(1, 10000, ErrorMessage = "Parametr limit musi mieścić się w przedziale 1–10000.")] int? limit)
    {
        var errors = new List<FieldError>();
        var seriesIds = ParseIds(series);
        if (seriesIds is null)
        {
            errors.Add(new FieldError("series", "Parametr series musi być listą liczb rozdzielonych przecinkami."));
        }

        if (sort is not (null or "timestamp" or "-timestamp"))
        {
            errors.Add(new FieldError("sort", "Parametr sort przyjmuje wartości timestamp lub -timestamp."));
        }

        if (from > to)
        {
            errors.Add(new FieldError("from", "Początek przedziału nie może być późniejszy niż jego koniec."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Create(HttpContext, StatusCodes.Status400BadRequest, "Niepoprawne żądanie",
                "Parametry zapytania zawierają błędy.", errors);
        }

        var query = db.Measurements.AsNoTracking();
        if (seriesIds!.Count > 0)
        {
            query = query.Where(m => seriesIds.Contains(m.SeriesId));
        }

        if (from is { } start)
        {
            var startUtc = start.ToUniversalTime();
            query = query.Where(m => m.Timestamp >= startUtc);
        }

        if (to is { } end)
        {
            var endUtc = end.ToUniversalTime();
            query = query.Where(m => m.Timestamp <= endUtc);
        }

        query = sort == "-timestamp"
            ? query.OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id)
            : query.OrderBy(m => m.Timestamp).ThenBy(m => m.Id);

        return Ok(await query.Take(limit ?? DefaultLimit).Select(MeasurementDto.Projection).ToListAsync());
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        var measurement = await db.Measurements.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(MeasurementDto.Projection)
            .SingleOrDefaultAsync();
        return measurement is null
            ? ApiProblems.NotFound(HttpContext, $"Wynik o identyfikatorze {id} nie istnieje.")
            : Ok(measurement);
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    [HttpDelete("{id:long}")]
    public IActionResult Modify()
    {
        Response.Headers.Allow = "GET";
        return ApiProblems.Create(HttpContext, StatusCodes.Status405MethodNotAllowed, "Niedozwolona metoda",
            "Wyników pomiarów nie można modyfikować ani usuwać.");
    }

    [SensorOnly]
    [HttpPost]
    public async Task<IActionResult> Create(MeasurementInput input)
    {
        var sensorId = User.SensorId();
        var seriesId = User.SeriesId();
        var now = clock.GetUtcNow();
        var value = input.Value!.Value;
        var timestamp = (input.Timestamp ?? now).ToUniversalTime();

        if (timestamp > now + MaxClockSkew)
        {
            return await RejectAsync(seriesId, sensorId, value, timestamp, new FieldError("timestamp",
                "Znacznik czasu nie może być późniejszy niż 5 minut od bieżącego czasu serwera."));
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        var series = (await db.Series.FromSql($"SELECT * FROM series WHERE id = {seriesId} FOR SHARE").ToListAsync())
            .SingleOrDefault();
        if (series is null)
        {
            return ApiProblems.Create(HttpContext, StatusCodes.Status401Unauthorized, "Brak autoryzacji",
                "Seria przypisana do czujnika już nie istnieje.");
        }

        if (value < series.MinValue || value > series.MaxValue)
        {
            await tx.RollbackAsync();
            return await RejectAsync(seriesId, sensorId, value, timestamp, new FieldError("value",
                Invariant($"Wartość {value} jest spoza dopuszczalnego przedziału serii [{series.MinValue}, {series.MaxValue}].")),
                new Dictionary<string, object?> { ["minValue"] = series.MinValue, ["maxValue"] = series.MaxValue });
        }

        var measurement = new Measurement
        {
            SeriesId = seriesId,
            SensorId = sensorId,
            Value = value,
            Timestamp = timestamp,
            ReceivedAt = now,
        };
        db.Measurements.Add(measurement);
        await db.Sensors
            .Where(s => s.Id == sensorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastMeasurementAt, now));
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Created($"/api/measurements/{measurement.Id}", MeasurementDto.From(measurement));
    }

    private async Task<IActionResult> RejectAsync(int seriesId, int sensorId, double value, DateTimeOffset timestamp,
        FieldError error, IDictionary<string, object?>? extensions = null)
    {
        var payload = JsonSerializer.Serialize(new { value, timestamp });
        await events.WarnAsync(EventKinds.MeasurementRejected, error.Message, seriesId, sensorId, payload);
        return ApiProblems.Create(HttpContext, StatusCodes.Status422UnprocessableEntity, "Wynik odrzucony",
            error.Message, [error], extensions);
    }

    private static List<int>? ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var ids = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return null;
            }

            ids.Add(id);
        }

        return ids;
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
