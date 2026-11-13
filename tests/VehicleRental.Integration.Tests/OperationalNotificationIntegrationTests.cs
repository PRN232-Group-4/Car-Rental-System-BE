using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Features.Customers;
using Car_Rental_System_BE.Infrastructure.Features.Reports;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace VehicleRental.Tests;

public sealed class OperationalNotificationIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_commits_and_recipient_queries_work_even_when_delivery_fails(bool fail)
    {
        var (customerId, customerEmail) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var repository = new NpgsqlRentalBookingRepository(db.DataSource);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(5), DateTimeKind.Unspecified);
        var rental = await repository.CreateRentalAsync(customerEmail, vehicle, null, "hourly", start, start.AddHours(2), false, null, false, default);
        await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_status='pending' WHERE id=$1", customerId);
        var reporterEmail = $"reporter-{Guid.NewGuid():N}@test.local";
        var reporterUser = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES($1,'Reporter') RETURNING id", reporterEmail);
        var reporter = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id) VALUES($1) RETURNING id", reporterUser))!;
        var commitVisible = new List<bool>();
        int reportId = 0;
        var expectedReportStatus = "open";
        var sender = new OperationalNotificationTests.Sender(async (_, subject, _, _) =>
        {
            // Queries are outside the operation transaction, proving data committed before attempting email.
            if (subject.Contains("giấy phép"))
                commitVisible.Add((string?)await Seed.ScalarAsync(db.DataSource, "SELECT license_status FROM customers WHERE id=$1", customerId) == "approved");
            else if (subject.Contains("báo cáo"))
            {
                var value = await Seed.ScalarAsync(db.DataSource, "SELECT status FROM reports WHERE rental_id=$1 ORDER BY id DESC LIMIT 1", rental.Id);
                commitVisible.Add((string?)value == expectedReportStatus);
            }
            else
                commitVisible.Add((string?)await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", rental.Id) == "pre_inspection");
            if (fail) throw new HttpRequestException("Provider unavailable");
        });
        var recipients = new NpgsqlOperationalNotificationRecipients(db.DataSource);
        var notifications = OperationalNotificationTests.Notifications(sender, recipients);
        var reports = new ReportService(new NpgsqlReportRepository(db.DataSource), notifications);
        var licenses = new CustomerLicenseService(new NpgsqlCustomerLicenseRepository(db.DataSource), notifications: notifications);
        Assert.Equal("approved", (await licenses.ReviewLicenseAsync(customerId, "approved", default)).Status);
        var lifecycle = new RentalLifecycleService(new NpgsqlRentalLifecycleRepository(db.DataSource), null!, null!, null!, null!,
            new RentalEventSink(NullLogger<RentalEventSink>.Instance, notifications), new(), new(), TimeProvider.System);
        Assert.Equal("pre_inspection", (await lifecycle.ConfirmAsync(rental.Id, new(1, "manager@test.local", "Manager"), default)).ToStatus);
        var report = await reports.CreateReportAsync(rental.Id, reporter, false, "damage", "private evidence description", "private images", default);
        reportId = report.Id;
        expectedReportStatus = "in_progress";
        var completion = new ReportService(new NpgsqlReportRepository(db.DataSource), notifications);
        Assert.Equal("in_progress", (await completion.SetReportStatusAsync(reportId, new("in_progress", "Internal review"), "manager@test.local", default)).Status);
        Assert.Equal(6, sender.Messages.Count);
        Assert.Equal(6, commitVisible.Count);
        Assert.All(commitVisible, visible => Assert.True(visible));
        var reportEmails = sender.Messages.Where(m => m.Subject.Contains("báo cáo")).ToArray();
        Assert.Equal(4, reportEmails.Length);
        Assert.Equal(2, reportEmails.Count(m => m.Recipient.Email == customerEmail));
        Assert.Equal(2, reportEmails.Count(m => m.Recipient.Email == reporterEmail));
        Assert.All(sender.Messages, m => Assert.DoesNotContain("private", m.Text));
        Assert.Equal("in_progress", (string?)await Seed.ScalarAsync(db.DataSource, "SELECT status FROM reports WHERE id=$1", reportId));
    }

    [Fact]
    public async Task Recipient_queries_skip_inactive_accounts_and_deduplicate_same_customer_reporter()
    {
        var (customer, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var repository = new NpgsqlRentalBookingRepository(db.DataSource);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), DateTimeKind.Unspecified);
        var rental = await repository.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(1), false, null, false, default);
        var staff = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id) SELECT user_id FROM customers WHERE id=$1 RETURNING id", customer))!;
        var report = await new NpgsqlReportRepository(db.DataSource).CreateReportAsync(rental.Id, staff, false, "damage", "Description", null, default);
        var recipients = new NpgsqlOperationalNotificationRecipients(db.DataSource);
        Assert.Equal(email, Assert.Single((await recipients.ReportAsync(report.Id, default))!.Recipients).Email);
        await Seed.ScalarAsync(db.DataSource, "UPDATE users SET is_active=false WHERE email=$1", email);
        Assert.Null(await recipients.CustomerAsync(customer, default));
        Assert.Null(await recipients.RentalCustomerAsync(rental.Id, default));
        Assert.Empty((await recipients.ReportAsync(report.Id, default))!.Recipients);
    }
}
