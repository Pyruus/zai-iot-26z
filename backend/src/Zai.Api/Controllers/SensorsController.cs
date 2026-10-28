using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zai.Api.Auth;
using Zai.Api.Contracts;
using Zai.Api.Data;
using Zai.Api.Domain;
using Zai.Api.Infrastructure;

namespace Zai.Api.Controllers;

[ApiController]
[AdminOnly]
[Route("api/sensors")]
public sealed class SensorsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    private const string KeyPrefix = "zai_";

    [HttpGet]
    public async Task<List<SensorDto>> List() =>
        await db.Sensors.AsNoTracking()
            .Where(s => s.RevokedAt == null)
            .OrderBy(s => s.Id)
            .Select(SensorDto.Projection)
            .ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var sensor = await db.Sensors.AsNoTracking()
            .Where(s => s.Id == id && s.RevokedAt == null)
            .Select(SensorDto.Projection)
            .SingleOrDefaultAsync();
        return sensor is null ? NotFoundProblem(id) : Ok(sensor);
    }

    [HttpPost]
    public async Task<IActionResult> Register(SensorInput input)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors.Add(new FieldError("name", "Nazwa nie może składać się z samych spacji."));
        }

        var series = await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == input.SeriesId);
        if (series is null)
        {
            errors.Add(new FieldError("seriesId", $"Seria o identyfikatorze {input.SeriesId} nie istnieje."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Unprocessable(HttpContext, errors);
        }

        var apiKey = KeyPrefix + Secrets.GenerateToken();
        var sensor = new Sensor
        {
            Name = input.Name!.Trim(),
            SeriesId = series!.Id,
            ApiKeyHash = Secrets.Hash(apiKey),
            CreatedAt = clock.GetUtcNow(),
        };
        db.Sensors.Add(sensor);
        await db.SaveChangesAsync();

        return Created($"/api/sensors/{sensor.Id}",
            new SensorCreatedDto(sensor.Id, sensor.Name, series.Id, series.Name, sensor.CreatedAt, null, apiKey));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Unregister(int id)
    {
        var now = clock.GetUtcNow();
        var revoked = await db.Sensors
            .Where(s => s.Id == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
        return revoked == 0 ? NotFoundProblem(id) : NoContent();
    }

    private ObjectResult NotFoundProblem(int id) =>
        ApiProblems.NotFound(HttpContext, $"Czujnik o identyfikatorze {id} nie istnieje.");
}
