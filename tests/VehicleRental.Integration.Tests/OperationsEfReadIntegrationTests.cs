using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Rentals;
using System.Text;
using Xunit;

namespace VehicleRental.Tests;

public sealed class OperationsEfReadIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Rental_reads_preserve_customer_access_and_missing_optional_relations()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var (_, otherEmail) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var operations = new NpgsqlRentalBookingRepository(db.DataSource);
        var start = DateTime.UtcNow.AddDays(2);
        var rental = await operations.CreateRentalAsync(email, vehicleId, null, "longterm", start,
            start.AddDays(30), false, null, false, default);

        var owned = await operations.GetRentalForUserAsync(rental.Id, email, false, true, false, default);
        Assert.NotNull(owned);
        Assert.Equal(rental.Id, owned.Id);
        Assert.Null(owned.HourlyPriceSnapshot);
        Assert.False(owned.HasDriver);
        Assert.Null(await operations.GetRentalForUserAsync(rental.Id, otherEmail, false, true, false, default));
        Assert.NotNull(await operations.GetRentalForUserAsync(rental.Id, otherEmail, true, false, false, default));

        var schedule = await new NpgsqlAssignmentRepository(db.DataSource).GetRentalScheduleAsync(rental.Id, default);
        Assert.NotNull(schedule);
        Assert.Equal("approved", schedule.CustomerLicenseStatus);

        var documents = new NpgsqlRentalContractDocumentRepository(db.DataSource);
        var service = new RentalContractDocumentService(new RentalBookingService(operations), documents, TimeProvider.System);
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF\n");
        using var content = new MemoryStream(pdf);
        var saved = await service.UploadAsync(rental.Id, content, "contract.pdf", "application/pdf",
            DateTime.UtcNow.AddMinutes(-1), new ActorContext(null, email, "Customer"), default);
        Assert.True(saved.HasUploadedContract);
        var document = await service.DownloadAsync(rental.Id, new ActorContext(null, email, "Customer"), default);
        Assert.Equal(pdf, document.Content);
    }
}
