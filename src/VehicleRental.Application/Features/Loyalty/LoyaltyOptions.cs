using Car_Rental_System_BE.Domain.Features.Loyalty;

namespace Car_Rental_System_BE.Application.Features.Loyalty;

public sealed class LoyaltyOptions
{
    public const string SectionName = "Loyalty";

    public int VndPerPoint { get; set; } = 10000;
    public List<LoyaltyTierOption> Tiers { get; set; } =
    [
        new() { Name = "Member", ThresholdPoints = 0, Multiplier = 1.00m },
        new() { Name = "Silver", ThresholdPoints = 500, Multiplier = 1.10m },
        new() { Name = "Gold", ThresholdPoints = 2000, Multiplier = 1.25m },
        new() { Name = "Platinum", ThresholdPoints = 5000, Multiplier = 1.50m }
    ];
    public RedemptionOptions Redemption { get; set; } = new();

    public IReadOnlyList<LoyaltyTierConfig> ToTierConfigs() =>
        Tiers.Select(t => new LoyaltyTierConfig(t.Name, t.ThresholdPoints, t.Multiplier)).ToList();

    public void Validate()
    {
        LoyaltyDomainRules.ValidateConfiguration(
            VndPerPoint,
            ToTierConfigs(),
            Redemption.PointValueVnd,
            Redemption.MinPoints,
            Redemption.StepPoints,
            Redemption.MaxPercentOfRent);
    }
}

public sealed class LoyaltyTierOption
{
    public string Name { get; set; } = string.Empty;
    public int ThresholdPoints { get; set; }
    public decimal Multiplier { get; set; } = 1.0m;
}

public sealed class RedemptionOptions
{
    public bool Enabled { get; set; } = false;
    public int PointValueVnd { get; set; } = 100;
    public int MinPoints { get; set; } = 50;
    public int StepPoints { get; set; } = 10;
    public decimal MaxPercentOfRent { get; set; } = 30m;
}
