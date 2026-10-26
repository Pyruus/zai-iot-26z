using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zai.Api.Auth;
using Zai.Api.Contracts;
using Zai.Api.Data;
using Zai.Api.Domain;
using Zai.Api.Infrastructure;

namespace Zai.Api.Controllers;

[ApiController]
[Route("api/series")]
public sealed class SeriesController(AppDbContext db, TimeProvider clock, EventLogger events) : ControllerBase
{
    [HttpGet]
    public async Task<List<SeriesDto>> List() =>
        await db.Series.AsNoTracking().OrderBy(s => s.Id).Select(SeriesDto.Projection).ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var series = await db.Series.AsNoTracking().Where(s => s.Id == id).Select(SeriesDto.Projection)
            .SingleOrDefaultAsync();
        return series is null ? NotFoundProblem(id) : Ok(series);
    }

    [AdminOnly]
    [HttpPost]
    public async Task<IActionResult> Create(SeriesInput input)
    {
        var errors = input.Validate();
        if (errors.Count > 0)
        {
            return ApiProblems.Unprocessable(HttpContext, errors);
        }

        var now = clock.GetUtcNow();
        var series = new Series { Name = "", Color = "", CreatedAt = now, UpdatedAt = now };
        input.ApplyTo(series);
        db.Series.Add(series);
        await db.SaveChangesAsync();

        return Created($"/api/series/{series.Id}", SeriesDto.From(series));
    }

    [AdminOnly]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SeriesInput input)
    {
        var errors = input.Validate();
        if (errors.Count > 0)
        {
            return ApiProblems.Unprocessable(HttpContext, errors);
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        var series = (await db.Series.FromSql($"SELECT * FROM series WHERE id = {id} FOR UPDATE").ToListAsync())
            .SingleOrDefault();
        if (series is null)
        {
            return NotFoundProblem(id);
        }

        var min = input.MinValue!.Value;
        var max = input.MaxValue!.Value;
        var stats = await db.Measurements
            .Where(m => m.SeriesId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Min = g.Min(m => m.Value),
                Max = g.Max(m => m.Value),
                Outside = g.Count(m => m.Value < min || m.Value > max),
            })
            .SingleOrDefaultAsync();

        if (stats is { Outside: > 0 })
        {
            await tx.RollbackAsync();
            var detail = string.Create(CultureInfo.InvariantCulture,
                $"Nowy przedział [{min}, {max}] wykluczyłby {stats.Outside} zapisanych wyników (zakres wartości w serii: [{stats.Min}, {stats.Max}]).");
            await events.WarnAsync(EventKinds.RangeConflict, detail, seriesId: id);
            return ApiProblems.Create(HttpContext, StatusCodes.Status409Conflict, "Konflikt z zapisanymi wynikami",
                detail, extensions: new Dictionary<string, object?>
                {
                    ["currentMin"] = stats.Min,
                    ["currentMax"] = stats.Max,
                    ["conflictingCount"] = stats.Outside,
                });
        }

        input.ApplyTo(series);
        series.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(SeriesDto.From(series));
    }

    [AdminOnly]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await db.Series.Where(s => s.Id == id).ExecuteDeleteAsync();
        return deleted == 0 ? NotFoundProblem(id) : NoContent();
    }

    private ObjectResult NotFoundProblem(int id) => ApiProblems.NotFound(HttpContext, $"Seria o identyfikatorze {id} nie istnieje.");
}
