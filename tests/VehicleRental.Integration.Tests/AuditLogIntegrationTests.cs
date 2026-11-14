using Car_Rental_System_BE.Infrastructure.Persistence;
using Xunit;
using Car_Rental_System_BE.Infrastructure.Features.Audit;

namespace VehicleRental.Tests;

public sealed class AuditLogIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Directory_filters_both_identities_maps_legacy_actions_and_keeps_consistent_pages()
    {
        var actor = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES('audit-actor@test.local','Audit Administrator') RETURNING id"))!;
        var target = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES('audit-target@test.local','Audit Staff') RETURNING id"))!;
        var timestamp = new DateTime(2026, 10, 21, 18, 30, 0, DateTimeKind.Utc);
        var actions = new[] { "role:add", "role:remove", "deactivate", "activate", "sessions:revoke", "future_action" };
        var mapped = new[] { "role_assigned", "role_removed", "account_locked", "account_unlocked", "session_revoked", "future_action" };
        var ids = new List<long>();
        foreach (var action in actions)
            ids.Add((long)(await Seed.ScalarAsync(db.DataSource,
                "INSERT INTO user_administration_audit(user_id,actor_user_id,action,reason,created_at,ip_address) VALUES($1,$2,$3,'Role or account change',$4,'14.238.102.55') RETURNING id",
                target, actor, action, timestamp))!);
        var repository = new NpgsqlAuditLogRepository(db.DataSource);
        var first = await repository.ListAsync(new() { PageSize = 1 }, default);
        Assert.Equal(6, first.TotalCount);
        var latest = Assert.Single(first.Items);
        Assert.Equal($"AUD-{ids[^1]}", latest.Id);
        Assert.Equal(timestamp, latest.Timestamp);
        Assert.Equal(DateTimeKind.Utc, latest.Timestamp.Kind);
        Assert.Equal(actor, latest.ActorId);
        Assert.Equal("audit-actor@test.local", latest.ActorEmail);
        Assert.Equal("Audit Administrator", latest.ActorName);
        Assert.Equal(target, latest.TargetUserId);
        Assert.Equal("audit-target@test.local", latest.TargetUser);
        Assert.Equal("Role or account change", latest.Details);
        Assert.Equal("14.238.102.55", latest.IpAddress);
        Assert.Equal("success", latest.Status);
        var second = await repository.ListAsync(new() { Page = 2, PageSize = 1 }, default);
        Assert.Equal($"AUD-{ids[^2]}", Assert.Single(second.Items).Id);
        Assert.Equal(6, second.TotalCount);
        foreach (var search in new[] { "ACTOR@TEST", "Administrator", "TARGET@TEST", "Staff", actor.ToString(), target.ToString() })
            Assert.Equal(6, (await repository.ListAsync(new() { Search = search }, default)).TotalCount);
        for (var i = 0; i < mapped.Length; i++)
        {
            var page = await repository.ListAsync(new() { Action = mapped[i], Search = "Staff" }, default);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal($"AUD-{ids[i]}", Assert.Single(page.Items).Id);
            Assert.Equal(mapped[i], page.Items[0].Action);
        }
        var byAuditId = await repository.ListAsync(new() { Search = $"AUD-{ids[0]}" }, default);
        Assert.Equal($"AUD-{ids[0]}", Assert.Single(byAuditId.Items).Id);
        var empty = await repository.ListAsync(new() { Action = "role_assigned", Page = int.MaxValue, PageSize = 100 }, default);
        Assert.Empty(empty.Items);
        Assert.Equal(1, empty.TotalCount);
        Assert.Equal(int.MaxValue, empty.Page);
        Assert.Equal(100, empty.PageSize);
        Assert.Empty((await repository.ListAsync(new() { Action = "unknown_action" }, default)).Items);
        Assert.Empty((await repository.ListAsync(new() { Search = "%" }, default)).Items);

        // Foreign keys preserve identities referenced by immutable audit history.
        var deletion = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, "DELETE FROM users WHERE id=$1 OR id=$2", actor, target));
        Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, deletion.SqlState);
        var retained = Assert.Single((await repository.ListAsync(new() { Action = "role_assigned" }, default)).Items);
        Assert.Equal("audit-actor@test.local", retained.ActorEmail);
        Assert.Equal("Audit Administrator", retained.ActorName);
        Assert.Equal("audit-target@test.local", retained.TargetUser);
        Assert.Equal(actor, retained.ActorId);
        Assert.Equal(target, retained.TargetUserId);
        await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO user_administration_audit(user_id,actor_user_id,action) VALUES($1,$2,'legacy_action')", target, actor);
        var legacy = Assert.Single((await repository.ListAsync(new() { Action = "legacy_action" }, default)).Items);
        Assert.Null(legacy.Details);
        Assert.Null(legacy.IpAddress);
        Assert.Equal("success", legacy.Status);
    }
}
