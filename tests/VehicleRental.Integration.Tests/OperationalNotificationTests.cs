using System.Net;
using System.Reflection;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Features.Recovery;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Services;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Car_Rental_System_BE.Domain.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests;

public sealed class OperationalNotificationTests
{
    [Theory]
    [InlineData("approved", false)]
    [InlineData("approved", true)]
    [InlineData("rejected", false)]
    [InlineData("rejected", true)]
    public async Task License_review_commits_before_email_and_mail_failure_preserves_result(string status, bool fail)
    {
        var committed = false;
        var sentAfterCommit = false;
        var email = new Sender((_, _, _, _) => { sentAfterCommit = committed; return fail ? Task.FromException(new HttpRequestException("secret")) : Task.CompletedTask; });
        var repository = Proxy<ICustomerLicenseRepository>((method, _) =>
        {
            Assert.Equal("ReviewLicenseAsync", method.Name);
            committed = true;
            return Task.FromResult(new CustomerLicense("private", "B", null, null, "private", status));
        });
        var service = new CustomerLicenseService(repository, notifications: Notifications(email));
        Assert.Equal(status, (await service.ReviewLicenseAsync(12, status, default)).Status);
        Assert.Single(email.Messages);
        Assert.True(sentAfterCommit);
        Assert.Equal("customer@test.local", email.Messages[0].Recipient.Email);
        Assert.DoesNotContain("private", email.Messages[0].Text);
        Assert.Contains(status == "approved" ? "phê duyệt" : "từ chối", email.Messages[0].Text);
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("review", false)]
    [InlineData("review", true)]
    [InlineData("reject", false)]
    [InlineData("reject", true)]
    [InlineData("assign", false)]
    [InlineData("assign", true)]
    [InlineData("resolve", false)]
    [InlineData("resolve", true)]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    public async Task Report_workflow_notifies_customer_and_reporter_after_commit(string action, bool fail)
    {
        var committed = false;
        var sentAfterCommit = true;
        var email = new Sender((_, _, _, _) => { sentAfterCommit &= committed; return fail ? Task.FromException(new HttpRequestException()) : Task.CompletedTask; });
        var notifications = Notifications(email);
        var report = new IncidentReport(41, 72, 1, 2, "damage", "private report description", "open", null, "private image");
        var rental = Rental();
        var reportRepository = Proxy<IReportRepository>((method, _) =>
        {
            committed = true;
            return method.Name switch
            {
                "CreateReportAsync" => Task.FromResult(report),
                "ResolveReportAsync" => Task.CompletedTask,
                _ => throw new NotSupportedException(method.Name)
            };
        });
        var reportService = new ReportService(reportRepository, notifications);
        var repository = Proxy<IRecoveryRepository>((method, _) =>
        {
            committed = true;
            return method.Name switch
            {
                "ReplaceVehicleAsync" => Task.FromResult(rental),
                _ => throw new NotSupportedException(method.Name)
            };
        });
        var recovery = new RecoveryService(repository, notifications);
        if (action is "review" or "reject")
        {
            var completion = Proxy<IReportRepository>((_, _) =>
            {
                committed = true;
                return Task.FromResult(report with { Status = action == "review" ? "in_progress" : "rejected" });
            });
            await new ReportService(completion, notifications).SetReportStatusAsync(41,
                new(action == "review" ? "in_progress" : "rejected", "internal reason"), "manager@test.local", default);
        }
        else if (action == "create") await reportService.CreateReportAsync(72, 2, false, "damage", report.Description, null, default);
        else if (action == "assign") await new MaintenanceService(Proxy<IMaintenanceRepository>((method, _) =>
        {
            committed = true;
            return method.Name switch
            {
                "AssignReportMaintenanceAsync" => Task.FromResult(new Maintenance(5, 1, null, 2, "scheduled", "incident", null, false, null, null)),
                _ => throw new NotSupportedException(method.Name)
            };
        }), notifications).AssignReportMaintenanceAsync(41, 2, null, DateOnly.FromDateTime(DateTime.UtcNow), default);
        else if (action == "resolve") await reportService.ResolveReportAsync(41, "manager@test.local", default);
        else await recovery.ReplaceVehicleAsync(72, 1, 2, 41, "manager@test.local", default);
        var reportEmails = email.Messages.Where(m => m.Subject.Contains("báo cáo")).ToArray();
        Assert.Equal(2, reportEmails.Length);
        Assert.True(sentAfterCommit);
        Assert.Equal(new[] { "customer@test.local", "reporter@test.local" }, reportEmails.Select(m => m.Recipient.Email));
        Assert.All(reportEmails, message => { Assert.Contains("#41", message.Text); Assert.Contains("#72", message.Text); Assert.DoesNotContain("private", message.Text); Assert.DoesNotContain("internal", message.Text); });
    }

    [Theory]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    [InlineData("start", false)]
    [InlineData("start", true)]
    [InlineData("return", false)]
    [InlineData("return", true)]
    [InlineData("complete", false)]
    [InlineData("complete", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    public async Task Lifecycle_commands_notify_after_write_without_propagating_mail_errors(string action, bool fail)
    {
        var committed = false;
        var sentAfterCommit = false;
        var independentToken = false;
        var email = new Sender((_, _, _, ct) =>
        {
            sentAfterCommit = committed; independentToken = !ct.IsCancellationRequested;
            return fail ? Task.FromException(new TaskCanceledException()) : Task.CompletedTask;
        });
        var now = DateTime.UtcNow;
        var status = action switch { "start" => RentalStatus.PreInspection, "return" => RentalStatus.Renting, "complete" => RentalStatus.PostInspection, _ => RentalStatus.Pending };
        var entity = new RentalLifecycleEntity(72, 12, 1, null, null, false, "hourly", status, 10, 0, 0, 110,
            now.AddHours(-1), now.AddHours(1), false, 100, 0, 0, 10, null, now.AddHours(-1), now, null, null, null, null, 0);
        var repo = Proxy<IRentalLifecycleRepository>((method, args) => method.Name switch
        {
            "GetAsync" => Task.FromResult<RentalLifecycleEntity?>(entity),
            "HasPassingPreInspectionAsync" or "HasValidAssignmentAsync" or "IsCustomerLicenseValidAsync" or "HasPostInspectionAsync" or "IsPostInspectionOkAsync" => Task.FromResult(true),
            "SettleCompletionAsync" => Settle((SettlementPlan)args[1]!),
            _ when method.Name.StartsWith("UpdateTo") => Commit(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Commit() { committed = true; return Task.CompletedTask; }
        Task<RentalSettlementDto> Settle(SettlementPlan plan)
        {
            Commit();
            return Task.FromResult(new RentalSettlementDto(72, plan.ScheduledEnd, plan.ReturnedAt, plan.OvertimeMinutes,
                plan.OvertimeHours, plan.OvertimeAmount, plan.OvertimeVat, plan.DepositHeld, plan.DepositDeducted,
                null, null, plan.DamageReviewRequired, plan.CompletedAt));
        }
        var gate = Proxy<IRentalPaymentGate>((method, _) => method.Name switch
        {
            "IsRentalInvoicePaidAsync" => Task.FromResult(true),
            "GetDepositHeldAsync" or "GetTotalPaidAmountAsync" => Task.FromResult(0m),
            "VoidUnpaidInvoiceAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(method.Name)
        });
        var loyalty = Proxy<ILoyaltyAward>((_, _) => Task.CompletedTask);
        var service = new RentalLifecycleService(repo, gate, null!, null!, loyalty,
            new RentalEventSink(NullLogger<RentalEventSink>.Instance, Notifications(email)), new(), new(), TimeProvider.System);
        var actor = new ActorContext(1, "manager@test.local", "Manager");
        var result = action switch
        {
            "confirm" => await service.ConfirmAsync(72, actor, default),
            "start" => await service.StartAsync(72, actor, default),
            "return" => await service.ReturnAsync(72, null, actor, default),
            "complete" => await service.CompleteAsync(72, actor, default),
            _ => await service.CancelAsync(72, new("Branch cancellation", "branch"), actor, default)
        };
        Assert.Equal(72, result.RentalId);
        Assert.Single(email.Messages);
        Assert.True(sentAfterCommit);
        Assert.True(independentToken);
        Assert.Equal("customer@test.local", email.Messages[0].Recipient.Email);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Refund_reviews_notify_after_commit_even_when_mail_fails(bool approve, bool fail)
    {
        var committed = false;
        var sentAfterCommit = false;
        var email = new Sender((_, _, _, _) => { sentAfterCommit = committed; return fail ? Task.FromException(new HttpRequestException()) : Task.CompletedTask; });
        var repository = Proxy<IPaymentRepository>((_, _) =>
        {
            committed = true;
            return Task.FromResult(new PaymentSummary(4, 72, null, "refund", 10, approve ? "paid" : "rejected", null, null, null, DateTime.UtcNow));
        });
        var result = await new PaymentService(repository, null!, new("https://test.local", "https://test.local"), Notifications(email))
            .ReviewRefundAsync(4, approve, "manager@test.local", default);
        Assert.Equal(approve ? "paid" : "rejected", result.Status);
        Assert.Contains("#4", Assert.Single(email.Messages).Text);
        Assert.True(sentAfterCommit);
    }

    [Theory]
    [InlineData("pre_rental", true)]
    [InlineData("pre_rental", false)]
    [InlineData("post_rental", true)]
    [InlineData("post_rental", false)]
    public async Task Inspection_results_notify_customer_after_evidence_is_saved(string type, bool isOk)
    {
        var committed = false;
        var sentAfterCommit = false;
        var email = new Sender((_, _, _, _) => { sentAfterCommit = committed; return Task.FromException(new HttpRequestException()); });
        var repository = Proxy<IInspectionRecordRepository>((_, _) =>
        {
            committed = true;
            return Task.FromResult(new Inspection(1, 72, type, isOk, null, null, "private", "private", DateTime.UtcNow));
        });
        var result = await new InspectionRecordService(repository, Notifications(email))
            .AddInspectionAsync(72, 2, type, isOk, null, null, "private", null, default);
        Assert.Equal(isOk, result.IsOk);
        Assert.Contains(isOk ? "đạt" : "không đạt", Assert.Single(email.Messages).Text);
        Assert.DoesNotContain("private", email.Messages[0].Text);
        Assert.True(sentAfterCommit);
    }

    [Theory]
    [InlineData("license")]
    [InlineData("rental")]
    [InlineData("report")]
    [InlineData("refund")]
    [InlineData("inspection")]
    public async Task Recipient_lookup_failure_is_best_effort_and_logs_do_not_include_secret_details(string kind)
    {
        var sender = new Sender();
        var log = new CaptureLogger<OperationalNotifications>();
        var recipients = Proxy<IOperationalNotificationRecipients>((_, _) => throw new InvalidOperationException("api-key secret customer@test.local"));
        var notifications = new OperationalNotifications(recipients, sender, log);
        await (kind switch
        {
            "license" => notifications.LicenseReviewedAsync(12, "approved"),
            "rental" => notifications.RentalStatusChangedAsync(72, "renting"),
            "report" => notifications.ReportChangedAsync(41, "open"),
            "refund" => notifications.RefundReviewedAsync(72, 4, true),
            _ => notifications.InspectionRecordedAsync(72, "pre_rental", true)
        });
        Assert.Empty(sender.Messages);
        Assert.Contains("InvalidOperationException", Assert.Single(log.Messages));
        Assert.DoesNotContain("secret", log.Messages[0]);
        Assert.DoesNotContain("@", log.Messages[0]);
        Assert.Null(log.Exception);
    }

    [Theory]
    [InlineData("license")]
    [InlineData("report")]
    [InlineData("refund")]
    public async Task Business_errors_still_propagate_and_do_not_send_mail(string kind)
    {
        var sender = new Sender();
        var notifications = Notifications(sender);
        var error = new InvalidOperationException("Business rule failed");
        var reports = new ReportService(Proxy<IReportRepository>((_, _) => throw error), notifications);
        var licenses = new CustomerLicenseService(Proxy<ICustomerLicenseRepository>((_, _) => throw error), notifications: notifications);
        var payment = new PaymentService(Proxy<IPaymentRepository>((_, _) => throw error), null!, new("https://test.local", "https://test.local"), notifications);
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => kind switch
        {
            "license" => licenses.ReviewLicenseAsync(12, "approved", default),
            "report" => reports.CreateReportAsync(72, 2, false, "damage", "description", null, default),
            _ => (Task)payment.ReviewRefundAsync(4, true, "manager@test.local", default)
        });
        Assert.Same(error, actual);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task Sink_uses_post_commit_token_independent_of_cancelled_request()
    {
        var independentToken = false;
        var sender = new Sender((_, _, _, ct) => { independentToken = !ct.IsCancellationRequested; return Task.CompletedTask; });
        await new RentalEventSink(NullLogger<RentalEventSink>.Instance, Notifications(sender))
            .PublishStatusChangedAsync(new(72, RentalStatus.Pending, RentalStatus.PreInspection, DateTime.UtcNow, new(1, "manager@test.local", "Manager")), new(true));
        Assert.Single(sender.Messages);
        Assert.True(independentToken);
    }

    [Theory]
    [InlineData(202)]
    [InlineData(503)]
    public async Task Brevo_sender_sends_plain_text_and_uses_safe_error_without_provider_body(int code)
    {
        JsonElement body = default;
        var client = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("https://api.brevo.com/v3/smtp/email", request.RequestUri!.ToString());
            Assert.Equal("test-api-key", Assert.Single(request.Headers.GetValues("api-key")));
            body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
            return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("private provider body") };
        }));
        var service = new BrevoOperationalEmailSender(client, Options.Create(new BrevoOptions { ApiKey = "test-api-key", SenderEmail = "sender@test.local", SenderName = "Thong Dong" }));
        if (code >= 400)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.SendAsync(new("customer@test.local", "Customer"), "Subject", "Text", default));
            Assert.DoesNotContain("private", error.ToString());
        }
        else await service.SendAsync(new("customer@test.local", "Customer"), "Subject", "Text", default);
        Assert.Equal("Text", body.GetProperty("textContent").GetString());
        Assert.Equal("customer@test.local", body.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.False(body.TryGetProperty("htmlContent", out _));
    }

    internal static OperationalNotifications Notifications(Sender sender, IOperationalNotificationRecipients? recipients = null) =>
        new(recipients ?? Proxy<IOperationalNotificationRecipients>((method, _) => method.Name switch
        {
            "CustomerAsync" or "RentalCustomerAsync" => Task.FromResult<OperationalEmailRecipient?>(new("customer@test.local", "Customer")),
            "ReportAsync" => Task.FromResult<OperationalReportRecipients?>(new(72, [new("customer@test.local", "Customer"), new("reporter@test.local", "Reporter")])),
            _ => throw new NotSupportedException(method.Name)
        }), sender, NullLogger<OperationalNotifications>.Instance);

    private static Rental Rental() => new(72, 12, 1, "hourly", "pre_inspection", false, 0, 0, 0, 100, null, null);
    internal sealed record Message(OperationalEmailRecipient Recipient, string Subject, string Text);
    internal sealed class Sender(Func<OperationalEmailRecipient, string, string, CancellationToken, Task>? callback = null) : IOperationalEmailSender
    {
        public List<Message> Messages { get; } = [];
        public Task SendAsync(OperationalEmailRecipient recipient, string subject, string text, CancellationToken ct)
        {
            Messages.Add(new(recipient, subject, text));
            return callback?.Invoke(recipient, subject, text, ct) ?? Task.CompletedTask;
        }
    }
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy;
    }
    public class Stub : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }
    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Messages.Add(formatter(state, exception)); Exception = exception; }
    }
}
