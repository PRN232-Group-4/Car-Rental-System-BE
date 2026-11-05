namespace Car_Rental_System_BE.Domain.Features.Loyalty;

public enum LoyaltyTransactionType
{
    Earn,
    Redeem,
    Reverse,
    Adjust
}

public static class LoyaltyTransactionTypeExtensions
{
    public static string ToDbString(this LoyaltyTransactionType type) => type switch
    {
        LoyaltyTransactionType.Earn => "earn",
        LoyaltyTransactionType.Redeem => "redeem",
        LoyaltyTransactionType.Reverse => "reverse",
        LoyaltyTransactionType.Adjust => "adjust",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static LoyaltyTransactionType FromDbString(string raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "earn" => LoyaltyTransactionType.Earn,
        "redeem" => LoyaltyTransactionType.Redeem,
        "reverse" => LoyaltyTransactionType.Reverse,
        "adjust" => LoyaltyTransactionType.Adjust,
        _ => throw new ArgumentException($"Invalid loyalty transaction type: '{raw}'", nameof(raw))
    };
}

public sealed record LoyaltyTierConfig(string Name, int ThresholdPoints, decimal Multiplier);

public sealed record NextTierInfo(string Name, int PointsNeeded);

public sealed record LoyaltyCustomerSummary(
    int Balance,
    string Tier,
    decimal Multiplier,
    int LifetimeEarned,
    NextTierInfo? NextTier,
    int VndPerPoint,
    int PointValueVnd,
    bool RedemptionEnabled);

public sealed record LoyaltyEarnResult(
    int EarnedPoints,
    int BasePoints,
    string TierName,
    decimal Multiplier);

public sealed record RedemptionQuote(
    int MaxPoints,
    decimal MaxDiscount,
    int PointValueVnd,
    bool IsEnabled);

public static class LoyaltyDomainRules
{
    public static readonly IReadOnlyList<LoyaltyTierConfig> DefaultTiers =
    [
        new("Member", 0, 1.00m),
        new("Silver", 500, 1.10m),
        new("Gold", 2000, 1.25m),
        new("Platinum", 5000, 1.50m)
    ];

    public static LoyaltyTierConfig ResolveTier(int lifetimeEarned, IReadOnlyList<LoyaltyTierConfig> tiers)
    {
        if (tiers is null || tiers.Count == 0)
            tiers = DefaultTiers;

        var ordered = tiers.OrderByDescending(t => t.ThresholdPoints).ToList();
        return ordered.FirstOrDefault(t => lifetimeEarned >= t.ThresholdPoints) ?? ordered.Last();
    }

    public static NextTierInfo? ResolveNextTier(int lifetimeEarned, IReadOnlyList<LoyaltyTierConfig> tiers)
    {
        if (tiers is null || tiers.Count == 0)
            tiers = DefaultTiers;

        var next = tiers.OrderBy(t => t.ThresholdPoints)
                        .FirstOrDefault(t => t.ThresholdPoints > lifetimeEarned);

        if (next is null) return null;
        return new NextTierInfo(next.Name, next.ThresholdPoints - lifetimeEarned);
    }

    public static LoyaltyEarnResult CalculateEarn(
        decimal basisVnd,
        int lifetimeEarned,
        int vndPerPoint,
        IReadOnlyList<LoyaltyTierConfig> tiers)
    {
        var tier = ResolveTier(lifetimeEarned, tiers);
        if (basisVnd <= 0 || vndPerPoint <= 0)
            return new LoyaltyEarnResult(0, 0, tier.Name, tier.Multiplier);

        var basePoints = (int)Math.Floor(basisVnd / vndPerPoint);
        if (basePoints <= 0)
            return new LoyaltyEarnResult(0, 0, tier.Name, tier.Multiplier);

        var earned = (int)Math.Floor(basePoints * tier.Multiplier);
        return new LoyaltyEarnResult(earned, basePoints, tier.Name, tier.Multiplier);
    }

    public static RedemptionQuote CalculateRedemptionQuote(
        decimal rentAmountBeforeVat,
        int currentBalance,
        bool enabled,
        int pointValueVnd,
        int minPoints,
        int stepPoints,
        decimal maxPercentOfRent)
    {
        if (!enabled)
            return new RedemptionQuote(0, 0, pointValueVnd, false);

        if (rentAmountBeforeVat <= 0 || currentBalance < minPoints || pointValueVnd <= 0)
            return new RedemptionQuote(0, 0, pointValueVnd, true);

        var maxDiscountByCap = rentAmountBeforeVat * (maxPercentOfRent / 100m);
        var maxDiscountByPoints = (decimal)currentBalance * pointValueVnd;
        var maxDiscountPossible = Math.Min(maxDiscountByCap, maxDiscountByPoints);

        var maxPoints = (int)Math.Floor(maxDiscountPossible / pointValueVnd);
        if (stepPoints > 0)
            maxPoints = (maxPoints / stepPoints) * stepPoints;

        if (maxPoints < minPoints)
            return new RedemptionQuote(0, 0, pointValueVnd, true);

        var actualDiscount = maxPoints * pointValueVnd;
        return new RedemptionQuote(maxPoints, actualDiscount, pointValueVnd, true);
    }

    public static void ValidateRedemption(
        int points,
        decimal rentAmountBeforeVat,
        int currentBalance,
        bool enabled,
        int pointValueVnd,
        int minPoints,
        int stepPoints,
        decimal maxPercentOfRent)
    {
        if (!enabled)
            throw new InvalidOperationException("Redemption is currently disabled.");

        if (points < minPoints)
            throw new ArgumentException($"Points to redeem must be at least {minPoints}.", nameof(points));

        if (stepPoints > 0 && points % stepPoints != 0)
            throw new ArgumentException($"Points to redeem must be a multiple of {stepPoints}.", nameof(points));

        if (points > currentBalance)
            throw new InvalidOperationException($"Insufficient points. Customer has {currentBalance}, attempted {points}.");

        var maxDiscountAllowed = rentAmountBeforeVat * (maxPercentOfRent / 100m);
        var discountRequested = (decimal)points * pointValueVnd;
        if (discountRequested > maxDiscountAllowed)
            throw new InvalidOperationException($"Discount ({discountRequested:N0} VND) exceeds allowed maximum of {maxPercentOfRent}% of rent ({maxDiscountAllowed:N0} VND).");
    }

    public static void ValidateConfiguration(
        int vndPerPoint,
        IReadOnlyList<LoyaltyTierConfig>? tiers,
        int pointValueVnd,
        int minPoints,
        int stepPoints,
        decimal maxPercentOfRent)
    {
        if (vndPerPoint <= 0)
            throw new ArgumentException("VndPerPoint must be positive.", nameof(vndPerPoint));

        if (tiers is null || tiers.Count == 0)
            throw new ArgumentException("Tiers configuration must not be empty.", nameof(tiers));

        var sorted = tiers.OrderBy(t => t.ThresholdPoints).ToList();
        if (sorted[0].ThresholdPoints != 0)
            throw new ArgumentException("The baseline tier must have ThresholdPoints = 0.", nameof(tiers));

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].ThresholdPoints <= sorted[i - 1].ThresholdPoints)
                throw new ArgumentException($"Tier threshold must be strictly increasing. '{sorted[i].Name}' ({sorted[i].ThresholdPoints}) <= '{sorted[i - 1].Name}' ({sorted[i - 1].ThresholdPoints}).", nameof(tiers));

            if (sorted[i].Multiplier < sorted[i - 1].Multiplier)
                throw new ArgumentException($"Tier multiplier must be non-decreasing. '{sorted[i].Name}' ({sorted[i].Multiplier}) < '{sorted[i - 1].Name}' ({sorted[i - 1].Multiplier}).", nameof(tiers));
        }

        foreach (var tier in sorted)
        {
            if (tier.Multiplier < 1.00m)
                throw new ArgumentException($"Tier multiplier must be >= 1.00. Tier '{tier.Name}' has {tier.Multiplier}.", nameof(tiers));
        }

        if (pointValueVnd <= 0)
            throw new ArgumentException("PointValueVnd must be positive.", nameof(pointValueVnd));

        if (minPoints <= 0)
            throw new ArgumentException("MinPoints must be positive.", nameof(minPoints));

        if (stepPoints <= 0)
            throw new ArgumentException("StepPoints must be positive.", nameof(stepPoints));

        if (maxPercentOfRent <= 0 || maxPercentOfRent > 100)
            throw new ArgumentException("MaxPercentOfRent must be between 1 and 100.", nameof(maxPercentOfRent));
    }
}
