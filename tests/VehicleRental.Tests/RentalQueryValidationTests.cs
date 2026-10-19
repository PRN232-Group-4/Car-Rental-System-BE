using System.Reflection;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalQueryValidationTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Application_rejects_bad_pagination_before_persistence(int page, int pageSize)
    {
        var repository = DispatchProxy.Create<IRentalQueryRepository, NeverCalled>();
        var service = new RentalQueryService(repository);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ListMyRentalsAsync(7, null, page, pageSize, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ListOperationalRentalsAsync(new(null, null, null, null, null, page, pageSize), default));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(int.MaxValue, 100)]
    public void Valid_bounds_and_large_pages_are_accepted(int page, int pageSize)
        => RentalQueryValidation.Pagination(page, pageSize);

    [Fact]
    public void Invalid_filter_references_dates_status_and_tab_are_rejected()
    {
        Assert.Throws<BusinessRuleException>(() => RentalQueryValidation.Operational(new(null, 0, null, null, null)));
        Assert.Throws<BusinessRuleException>(() => RentalQueryValidation.Operational(new(null, null, -1, null, null)));
        Assert.Throws<BusinessRuleException>(() => RentalQueryValidation.Operational(new("bad", null, null, null, null)));
        Assert.Throws<BusinessRuleException>(() => RentalQueryValidation.Operational(new(null, null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1))));
        Assert.Throws<BusinessRuleException>(() => RentalQueryValidation.CustomerTab("bad"));
    }

    public class NeverCalled : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new Exception("Invalid queries must not reach persistence.");
    }
}
