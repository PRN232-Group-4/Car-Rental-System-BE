using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Handovers;
using Car_Rental_System_BE.Application.Lifecycle;
using VehicleRental.Domain.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class HandoverServiceTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_pagination_is_rejected_before_query(int page, int pageSize)
    {
        var queries = new Queries();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new HandoverService(queries, new Lifecycle()).ListAsync(new(Page: page, PageSize: pageSize), default));
        Assert.Equal("INVALID_PAGINATION", error.Code);
        Assert.Null(queries.Filter);
    }

    [Theory]
    [InlineData("pending", 1)]
    [InlineData("completed", 100)]
    [InlineData("all", 20)]
    public async Task Queue_filters_and_pagination_are_forwarded_and_rows_use_actual_rental_status(string status, int size)
    {
        var queries = new Queries();
        var from = new DateTime(2032, 1, 1);
        var to = from.AddDays(1);
        var page = await new HandoverService(queries, new Lifecycle()).ListAsync(new(status, 3, 4, from, to, 2, size), default);
        Assert.Equal(new OperationalFilter(null, 3, 4, from, to, 2, size, status), queries.Filter);
        Assert.Equal(8, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(size, page.PageSize);
        Assert.Equal(["pending", "completed"], page.Items.Select(x => x.Status));
    }

    [Fact]
    public async Task Invalid_status_and_date_range_are_rejected()
    {
        var service = new HandoverService(new Queries(), new Lifecycle());
        Assert.Equal("INVALID_HANDOVER_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ListAsync(new("unknown"), default))).Code);
        Assert.Equal("INVALID_HANDOVER_FILTER", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ListAsync(new(From: DateTime.UtcNow, To: DateTime.UtcNow.AddDays(-1)), default))).Code);
    }

    [Fact]
    public async Task Default_queue_uses_pending_and_missing_detail_is_404()
    {
        var queries = new Queries();
        var service = new HandoverService(queries, new Lifecycle());
        await service.ListAsync(new(), default);
        Assert.Equal(new OperationalFilter(null, null, null, null, null, 1, 20, "pending"), queries.Filter);
        Assert.Equal("HANDOVER_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(19, default))).Code);
    }

    [Fact]
    public async Task Confirmation_delegates_id_and_actor_to_start_and_preserves_conflicts()
    {
        var lifecycle = new Lifecycle();
        var service = new HandoverService(new Queries(), lifecycle);
        var actor = new ActorContext(5, "manager@test.local", "Manager");
        Assert.Equal(new HandoverConfirmation(12, "completed", "renting"), await service.ConfirmAsync(12, actor, default));
        Assert.Equal(actor, lifecycle.Actor);
        lifecycle.Failure = new ConflictException("PRE_INSPECTION_REQUIRED", "Missing inspection.");
        Assert.Equal("PRE_INSPECTION_REQUIRED", (await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(12, actor, default))).Code);
    }

    private sealed class Queries : IRentalQueries
    {
        public OperationalFilter? Filter { get; private set; }
        public Task<PagedResult<OperationalRentalSummary>> ListOperationalRentalsAsync(OperationalFilter filter, CancellationToken ct)
        {
            Filter = filter;
            var pending = new OperationalRentalSummary(1, 3, "A", "a@test.local", 4, "51A", "Car", "pre_inspection",
                DateTime.UtcNow, DateTime.UtcNow.AddHours(2), 100, false, null);
            return Task.FromResult(new PagedResult<OperationalRentalSummary>([pending, pending with { Id = 2, Status = "completed" }],
                8, filter.Page, filter.PageSize));
        }
        public Task<OperationalRentalDetail?> GetOperationalDetailAsync(int id, CancellationToken ct) => Task.FromResult<OperationalRentalDetail?>(null);
        public Task<PagedResult<CustomerRentalSummary>> ListMyRentalsAsync(int customer, string? tab, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<CustomerRentalDetail?> GetMyRentalDetailAsync(int id, int customer, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<OverdueRentalItem>> ListOverdueRentalsAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Lifecycle : IRentalLifecycleService
    {
        public ActorContext? Actor { get; private set; }
        public ConflictException? Failure { get; set; }
        public Task<RentalLifecycleResult> StartAsync(int id, ActorContext actor, CancellationToken ct)
        {
            Actor = actor;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new RentalLifecycleResult(id, "pre_inspection", "renting", "Started."));
        }
        public Task<RentalLifecycleResult> ConfirmAsync(int id, ActorContext actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<RentalLifecycleResult> ReturnAsync(int id, DateTime? at, ActorContext actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<RentalLifecycleResult> CompleteAsync(int id, ActorContext actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<RentalLifecycleResult> CancelAsync(int id, CancelRentalCommand command, ActorContext actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<CancellationPreview> GetCancellationPreviewAsync(int id, ActorContext actor, CancellationToken ct) => throw new NotSupportedException();
    }
}
