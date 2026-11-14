using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>Các thao tác ghi khi duyệt dùng chung, chỉ được gọi sau khi bên gọi đã khóa và kiểm tra đơn thuê.</summary>
internal static class RentalApprovalWriter
{
    public static async Task WriteAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int rentalId,
        string? fromStatus, DateTime confirmedAt, ActorContext actor, string reason, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var confirmed = DateTime.SpecifyKind(confirmedAt, DateTimeKind.Unspecified);
        var now = await db.DatabaseNowAsync(ct);
        await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, "pre_inspection")
            .SetProperty(r => r.ConfirmedAt, confirmed)
            .SetProperty(r => r.StatusChangedAt, now), ct);
        await db.InsertAsync(new RentalStatusHistoryRow
        {
            RentalId = rentalId, FromStatus = fromStatus, ToStatus = "pre_inspection", ActorUserId = actor.UserId,
            ActorEmail = actor.Email, ActorRole = actor.Role, Reason = reason,
        }, ct);
        await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct);
    }
}
