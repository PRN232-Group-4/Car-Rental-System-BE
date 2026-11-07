using Car_Rental_System_BE.Domain.Loyalty;
using Xunit;

namespace VehicleRental.Tests;

public sealed class LoyaltyDomainTests
{
    private static readonly IReadOnlyList<LoyaltyTierConfig> Tiers = LoyaltyDomainRules.DefaultTiers;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5000, 0)]
    [InlineData(9999, 0)]
    [InlineData(10000, 1)]
    [InlineData(19999, 1)]
    [InlineData(20000, 2)]
    [InlineData(100000, 10)]
    public void Earn_calculation_rounds_down_base_points(decimal basisVnd, int expectedEarned)
    {
        // Member tier (multiplier 1.0)
        var result = LoyaltyDomainRules.CalculateEarn(basisVnd, lifetimeEarned: 0, vndPerPoint: 10000, Tiers);
        Assert.Equal(expectedEarned, result.EarnedPoints);
        Assert.Equal(expectedEarned, result.BasePoints);
        Assert.Equal("Member", result.TierName);
        Assert.Equal(1.00m, result.Multiplier);
    }

    [Theory]
    // Silver (multiplier 1.10):
    // 150_000 VND -> base 15 -> 15 * 1.10 = 16.5 -> 16
    [InlineData(150000, 500, "Silver", 1.10, 16)]
    // Gold (multiplier 1.25):
    // 110_000 VND -> base 11 -> 11 * 1.25 = 13.75 -> 13
    [InlineData(110000, 2000, "Gold", 1.25, 13)]
    // Platinum (multiplier 1.50):
    // 130_000 VND -> base 13 -> 13 * 1.50 = 19.5 -> 19
    [InlineData(130000, 5000, "Platinum", 1.50, 19)]
    public void Earn_calculation_applies_tier_multiplier_and_floors(
        decimal basisVnd, int lifetimeEarned, string expectedTier, decimal expectedMult, int expectedEarned)
    {
        var result = LoyaltyDomainRules.CalculateEarn(basisVnd, lifetimeEarned, vndPerPoint: 10000, Tiers);
        Assert.Equal(expectedTier, result.TierName);
        Assert.Equal(expectedMult, result.Multiplier);
        Assert.Equal(expectedEarned, result.EarnedPoints);
    }

    [Theory]
    [InlineData(0, "Member", "Silver", 500)]
    [InlineData(499, "Member", "Silver", 1)]
    [InlineData(500, "Silver", "Gold", 1500)]
    [InlineData(1999, "Silver", "Gold", 1)]
    [InlineData(2000, "Gold", "Platinum", 3000)]
    [InlineData(4999, "Gold", "Platinum", 1)]
    [InlineData(5000, "Platinum", null, 0)]
    [InlineData(10000, "Platinum", null, 0)]
    public void Tier_resolution_and_next_tier_progress_at_boundaries(
        int lifetimeEarned, string expectedCurrent, string? expectedNextTier, int expectedPointsNeeded)
    {
        var current = LoyaltyDomainRules.ResolveTier(lifetimeEarned, Tiers);
        Assert.Equal(expectedCurrent, current.Name);

        var next = LoyaltyDomainRules.ResolveNextTier(lifetimeEarned, Tiers);
        if (expectedNextTier is null)
        {
            Assert.Null(next);
        }
        else
        {
            Assert.NotNull(next);
            Assert.Equal(expectedNextTier, next.Name);
            Assert.Equal(expectedPointsNeeded, next.PointsNeeded);
        }
    }

    [Fact]
    public void Redemption_quote_respects_disabled_flag()
    {
        var quote = LoyaltyDomainRules.CalculateRedemptionQuote(
            rentAmountBeforeVat: 1_000_000,
            currentBalance: 500,
            enabled: false,
            pointValueVnd: 100,
            minPoints: 50,
            stepPoints: 10,
            maxPercentOfRent: 30);

        Assert.False(quote.IsEnabled);
        Assert.Equal(0, quote.MaxPoints);
        Assert.Equal(0m, quote.MaxDiscount);
    }

    [Fact]
    public void Redemption_quote_caps_at_thirty_percent_and_steps_by_ten()
    {
        // Rent 1,000,000 VND -> max 30% is 300,000 VND.
        // Customer has 4,000 points (worth 400,000 VND).
        // Capped discount = 300,000 VND -> 3,000 points.
        var quote = LoyaltyDomainRules.CalculateRedemptionQuote(
            rentAmountBeforeVat: 1_000_000,
            currentBalance: 4000,
            enabled: true,
            pointValueVnd: 100,
            minPoints: 50,
            stepPoints: 10,
            maxPercentOfRent: 30);

        Assert.True(quote.IsEnabled);
        Assert.Equal(3000, quote.MaxPoints);
        Assert.Equal(300_000m, quote.MaxDiscount);
    }

    [Fact]
    public void Redemption_quote_rounds_down_to_step_and_zeroes_below_minimum()
    {
        // Customer has 45 points (< 50 min)
        var quoteMin = LoyaltyDomainRules.CalculateRedemptionQuote(
            rentAmountBeforeVat: 500_000,
            currentBalance: 45,
            enabled: true,
            pointValueVnd: 100,
            minPoints: 50,
            stepPoints: 10,
            maxPercentOfRent: 30);
        Assert.Equal(0, quoteMin.MaxPoints);

        // Customer has 128 points -> step 10 -> 120 points (12,000 VND)
        var quoteStep = LoyaltyDomainRules.CalculateRedemptionQuote(
            rentAmountBeforeVat: 500_000,
            currentBalance: 128,
            enabled: true,
            pointValueVnd: 100,
            minPoints: 50,
            stepPoints: 10,
            maxPercentOfRent: 30);
        Assert.Equal(120, quoteStep.MaxPoints);
        Assert.Equal(12000m, quoteStep.MaxDiscount);
    }

    [Fact]
    public void Redemption_validation_enforces_all_invariants()
    {
        // Success case
        LoyaltyDomainRules.ValidateRedemption(
            points: 100,
            rentAmountBeforeVat: 500_000,
            currentBalance: 200,
            enabled: true,
            pointValueVnd: 100,
            minPoints: 50,
            stepPoints: 10,
            maxPercentOfRent: 30);

        // Disabled
        Assert.Throws<InvalidOperationException>(() =>
            LoyaltyDomainRules.ValidateRedemption(100, 500_000, 200, enabled: false, 100, 50, 10, 30));

        // Below min
        Assert.Throws<ArgumentException>(() =>
            LoyaltyDomainRules.ValidateRedemption(40, 500_000, 200, enabled: true, 100, 50, 10, 30));

        // Not step multiple
        Assert.Throws<ArgumentException>(() =>
            LoyaltyDomainRules.ValidateRedemption(105, 500_000, 200, enabled: true, 100, 50, 10, 30));

        // Insufficient points
        Assert.Throws<InvalidOperationException>(() =>
            LoyaltyDomainRules.ValidateRedemption(250, 500_000, 200, enabled: true, 100, 50, 10, 30));

        // Exceeds 30% of rent (500,000 * 30% = 150,000 VND = 1500 points max; request 1600)
        Assert.Throws<InvalidOperationException>(() =>
            LoyaltyDomainRules.ValidateRedemption(1600, 500_000, 2000, enabled: true, 100, 50, 10, 30));
    }

    [Fact]
    public void Configuration_validation_rejects_invalid_parameters()
    {
        // VndPerPoint <= 0
        Assert.Throws<ArgumentException>(() =>
            LoyaltyDomainRules.ValidateConfiguration(0, Tiers, 100, 50, 10, 30));

        // Baseline tier not 0
        var badBaseline = new List<LoyaltyTierConfig> { new("Member", 100, 1.0m) };
        Assert.Throws<ArgumentException>(() =>
            LoyaltyDomainRules.ValidateConfiguration(10000, badBaseline, 100, 50, 10, 30));

        // Non-increasing threshold
        var nonIncreasing = new List<LoyaltyTierConfig>
        {
            new("Member", 0, 1.0m),
            new("Silver", 500, 1.1m),
            new("Gold", 500, 1.25m)
        };
        Assert.Throws<ArgumentException>(() =>
            LoyaltyDomainRules.ValidateConfiguration(10000, nonIncreasing, 100, 50, 10, 30));

        // Multiplier < 1.0
        var lowMultiplier = new List<LoyaltyTierConfig>
        {
            new("Member", 0, 0.9m)
        };
        Assert.Throws<ArgumentException>(() =>
            LoyaltyDomainRules.ValidateConfiguration(10000, lowMultiplier, 100, 50, 10, 30));

        // MaxPercentOfRent > 100
        Assert.Throws<ArgumentException>(() =>
            LoyaltyDomainRules.ValidateConfiguration(10000, Tiers, 100, 50, 10, 150));
    }
}
