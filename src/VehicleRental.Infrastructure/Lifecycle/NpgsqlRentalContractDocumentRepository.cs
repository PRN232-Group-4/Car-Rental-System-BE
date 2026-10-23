using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Lifecycle;

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
                uploaded_at=(now() AT TIME ZONE 'utc');
            UPDATE longterm_rental_contract SET contract_link=$7,signed_at=$5 WHERE rental_id=$1
            """, connection, transaction))
        {
            save.Parameters.AddWithValue(rentalId);
            save.Parameters.AddWithValue(document.Content);
            save.Parameters.AddWithValue(document.ContentType);
            save.Parameters.AddWithValue(document.FileName);
            save.Parameters.Add(Sql.Timestamp("", signedAt));
            save.Parameters.AddWithValue(actor.Email);
            save.Parameters.AddWithValue($"/api/rentals/{rentalId}/contract/document");
            await save.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<RentalContractDocument?> GetAsync(int rentalId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT content,content_type,file_name FROM rental_contract_documents WHERE rental_id=$1");
        command.Parameters.AddWithValue(rentalId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetFieldValue<byte[]>(0), reader.GetString(1), reader.GetString(2)) : null;
    }
}
