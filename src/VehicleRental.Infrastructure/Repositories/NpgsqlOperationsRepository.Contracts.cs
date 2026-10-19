using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    public async Task<Rental> SaveRentalContractAsync(int id, string contractLink, DateTime signedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contractLink) || contractLink.Length > 255 ||
            !Uri.TryCreate(contractLink, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Contract link must be an HTTP(S) URL of at most 255 characters.");
        if (signedAt == default || signedAt > DateTime.UtcNow)
            throw new ArgumentException("Signed date must be provided and cannot be in the future.");
        // PostgreSQL timestamp precision is microseconds; API timestamps use UTC.
        if (signedAt.Kind == DateTimeKind.Local) signedAt = signedAt.ToUniversalTime();
        signedAt = new DateTime(signedAt.Ticks - signedAt.Ticks % 10, DateTimeKind.Unspecified);
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        var rental = await ReadRentalAsync(cn, tx, id, ct);
        if (rental.RentalType != "longterm")
            throw new InvalidOperationException("Only long-term rentals have a contract.");
        if (rental.ContractLink == contractLink && rental.SignedAt == signedAt) return rental;
        if (rental.Status is not ("pending" or "pre_inspection") || rental.SignedAt is not null)
            throw new InvalidOperationException("Signed contracts cannot be replaced or recorded after the rental starts.");
        await ValueAsync(cn, tx, "UPDATE longterm_rental_contract SET contract_link=$2,signed_at=$3 WHERE rental_id=$1", ct, id, contractLink, signedAt);
        await tx.CommitAsync(ct);
        return rental with { ContractLink = contractLink, SignedAt = signedAt };
    }
}
