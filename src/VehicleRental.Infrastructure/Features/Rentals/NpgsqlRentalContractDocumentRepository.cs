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
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var staff = actor.Role is "Admin" or "Manager" or "CustomerService";
        var visible = await db.Set<RentalsRow>().AnyAsync(r => r.Id == rentalId && (staff ||
            (from c in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on c.UserId equals u.Id
             where c.Id == r.CustomerId && u.Email == actor.Email && u.IsActive select c.Id).Any()), ct);
        var rental = visible ? await db.LockByKeyAsync<RentalsRow>(rentalId, ct) : null;
        if (rental is null) throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        if (rental.RentalType != "longterm") throw new ArgumentException("Only long-term rentals require a contract.");
        if (rental.Status is not ("pending" or "pre_inspection"))
            throw new ConflictException("CONTRACT_UPLOAD_CLOSED", "Contract can only be uploaded before the rental starts.");

        // Khóa dòng đơn thuê tuần tự hóa việc tải lên cho đơn này, nên đọc-rồi-ghi thay cho ON CONFLICT DO UPDATE.
        var signed = Sql.Ts(signedAt);
        var uploadedAt = await db.DatabaseUtcNowAsync(ct);
        var updated = await db.Set<RentalContractDocumentsRow>().Where(d => d.RentalId == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Content, document.Content)
            .SetProperty(d => d.ContentType, document.ContentType)
            .SetProperty(d => d.FileName, document.FileName)
            .SetProperty(d => d.SignedAt, signed)
            .SetProperty(d => d.UploadedBy, actor.Email)
            .SetProperty(d => d.UploadedAt, uploadedAt), ct);
        if (updated == 0)
            await db.InsertAsync(new RentalContractDocumentsRow
            {
                RentalId = rentalId, Content = document.Content, ContentType = document.ContentType,
                FileName = document.FileName, SignedAt = signed, UploadedBy = actor.Email,
            }, ct);
        var link = $"/api/rental/{rentalId}/contract/document";
        await db.Set<LongtermRentalContractRow>().Where(c => c.RentalId == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.ContractLink, link)
            .SetProperty(c => c.SignedAt, signed), ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<RentalContractDocument?> GetAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalContractDocumentsRow>().Where(d => d.RentalId == rentalId)
            .Select(d => new RentalContractDocument(d.Content, d.ContentType, d.FileName)).FirstOrDefaultAsync(ct);
    }
}
