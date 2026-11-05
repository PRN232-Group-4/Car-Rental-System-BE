using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Rentals;

public sealed class NpgsqlRentalContractDocumentRepository(NpgsqlDataSource dataSource) : IRentalContractDocumentRepository
{
    public async Task SaveAsync(int rentalId, RentalContractDocument document, DateTime signedAt, ActorContext actor, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var rental = new NpgsqlCommand("""
            SELECT r.rental_type,r.status FROM rentals r
            WHERE r.id=$1 AND ($3 OR EXISTS(SELECT 1 FROM customers c JOIN users u ON u.id=c.user_id
                WHERE c.id=r.customer_id AND u.email=$2 AND u.is_active)) FOR UPDATE OF r
            """, connection, transaction))
        {
            rental.Parameters.AddWithValue(rentalId);
            rental.Parameters.AddWithValue(actor.Email);
            rental.Parameters.AddWithValue(actor.Role is "Admin" or "Manager" or "CustomerService");
            await using var reader = await rental.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
            if (reader.GetString(0) != "longterm") throw new ArgumentException("Only long-term rentals require a contract.");
            if (reader.GetString(1) is not ("pending" or "pre_inspection"))
                throw new ConflictException("CONTRACT_UPLOAD_CLOSED", "Contract can only be uploaded before the rental starts.");
        }
        await using (var save = new NpgsqlCommand("""
            INSERT INTO rental_contract_documents(rental_id,content,content_type,file_name,signed_at,uploaded_by)
            VALUES($1,$2,$3,$4,$5,$6)
            ON CONFLICT(rental_id) DO UPDATE SET content=EXCLUDED.content,content_type=EXCLUDED.content_type,
                file_name=EXCLUDED.file_name,signed_at=EXCLUDED.signed_at,uploaded_by=EXCLUDED.uploaded_by,
                uploaded_at=(now() AT TIME ZONE 'utc')
            """, connection, transaction))
        {
            save.Parameters.AddWithValue(rentalId);
            save.Parameters.AddWithValue(document.Content);
            save.Parameters.AddWithValue(document.ContentType);
            save.Parameters.AddWithValue(document.FileName);
            save.Parameters.Add(Sql.Timestamp("", signedAt));
            save.Parameters.AddWithValue(actor.Email);
            await save.ExecuteNonQueryAsync(ct);
        }
        // Positional parameters use PostgreSQL's extended query protocol, which accepts
        // one statement per command. Both writes still commit or roll back together.
        await using (var projection = new NpgsqlCommand(
            "UPDATE longterm_rental_contract SET contract_link=$2,signed_at=$3 WHERE rental_id=$1", connection, transaction))
        {
            projection.Parameters.AddWithValue(rentalId);
            projection.Parameters.AddWithValue($"/api/rental/{rentalId}/contract/document");
            projection.Parameters.Add(Sql.Timestamp("", signedAt));
            await projection.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<RentalContractDocument?> GetAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalContractDocumentsRow>().Where(d => d.RentalId == rentalId)
            .Select(d => new RentalContractDocument(d.Content, d.ContentType, d.FileName)).FirstOrDefaultAsync(ct);
    }
}
