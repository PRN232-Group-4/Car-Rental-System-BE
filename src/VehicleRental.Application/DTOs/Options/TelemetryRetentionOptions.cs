using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Options;

public sealed class TelemetryRetentionOptions
{
    public const string SectionName = "TelemetryRetention";

    [Range(1, 365)]
    public int RetentionDays { get; init; } = 30;

    [Range(100, 50000)]
    public int PurgeBatchSize { get; init; } = 5000;

    [Range(1, 168)]
    public int PurgeIntervalHours { get; init; } = 24;
}
