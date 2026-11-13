namespace Car_Rental_System_BE.Application.Features.Rentals;

public sealed class RentalOptions
{
    public const string SectionName = "Rental";

    public int GraceMinutes { get; set; } = 15;
}

public sealed class RentalCancellationOptions
{
    public const string SectionName = "Rental:Cancellation";

    public string[] SelfCancelStatuses { get; set; } = ["pending", "pre_inspection"];
    public int FreeHoursBeforeStart { get; set; } = 24;
    public decimal LateFeePercent { get; set; } = 20m;
}
