using Zai.Api.Data;
using Zai.Api.Domain;

namespace Zai.Api.Infrastructure;

public static class EventKinds
{
    public const string LoginFailed = "login_failed";
    public const string MeasurementRejected = "measurement_rejected";
    public const string InvalidApiKey = "invalid_api_key";
    public const string RangeConflict = "range_conflict";
}

public sealed class EventLogger(AppDbContext db, TimeProvider clock, ILogger<EventLogger> logger)
{
    public async Task WarnAsync(string kind, string message, int? seriesId = null, int? sensorId = null,
        string? payload = null)
    {
        logger.LogWarning("{Kind}: {Message}", kind, message);
        db.EventLog.Add(new EventLogEntry
        {
            OccurredAt = clock.GetUtcNow(),
            Level = "warning",
            Kind = kind,
            Message = message,
            SeriesId = seriesId,
            SensorId = sensorId,
            Payload = payload,
        });
        await db.SaveChangesAsync();
    }
}
