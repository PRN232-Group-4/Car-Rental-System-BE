using Car_Rental_System_BE.Application.Features.Rentals;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

/// <summary>Shared approval writes, called only after the caller locks and validates the rental.</summary>
internal static class RentalApprovalWriter
{
    public static async Task WriteAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int rentalId,
        string? fromStatus, DateTime confirmedAt, ActorContext actor, string reason, CancellationToken ct)
    {
        await using var update = new NpgsqlCommand("UPDATE rentals SET status='pre_inspection',confirmed_at=$2,status_changed_at=now() WHERE id=$1", cn, tx);
        update.Parameters.AddWithValue(rentalId);
        update.Parameters.AddWithValue(NpgsqlDbType.Timestamp, DateTime.SpecifyKind(confirmedAt, DateTimeKind.Unspecified));
        await update.ExecuteNonQueryAsync(ct);
        await using var history = new NpgsqlCommand("INSERT INTO rental_status_history(rental_id,from_status,to_status,actor_user_id,actor_email,actor_role,reason,created_at) VALUES($1,$2,'pre_inspection',$3,$4,$5,$6,now())", cn, tx);
        history.Parameters.AddWithValue(rentalId);
        history.Parameters.AddWithValue((object?)fromStatus ?? DBNull.Value);
        history.Parameters.AddWithValue((object?)actor.UserId ?? DBNull.Value);
        history.Parameters.AddWithValue(actor.Email);
        history.Parameters.AddWithValue(actor.Role);
        history.Parameters.AddWithValue(reason);
        await history.ExecuteNonQueryAsync(ct);
        await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct);
    }
}
