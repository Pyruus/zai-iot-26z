using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zai.Api.Auth;
using Zai.Api.Contracts;
using Zai.Api.Data;

namespace Zai.Api.Controllers;

[ApiController]
[AdminOnly]
[Route("api/logs")]
public sealed class LogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<EventLogDto>> List(
        [FromQuery] string? kind,
        [FromQuery, Range(1, 1000, ErrorMessage = "Parametr limit musi mieścić się w przedziale 1–1000.")] int? limit)
    {
        var query = db.EventLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            query = query.Where(l => l.Kind == kind);
        }

        return await query
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.Id)
            .Take(limit ?? 100)
            .Select(l => new EventLogDto(l.Id, l.OccurredAt, l.Level, l.Kind, l.SeriesId, l.SensorId, l.Message))
            .ToListAsync();
    }
}
