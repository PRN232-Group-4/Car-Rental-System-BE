using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.Features.Rentals;

public sealed record RentalContractDocument(byte[] Content, string ContentType, string FileName);

public interface IRentalContractDocumentRepository
{
    Task SaveAsync(int rentalId, RentalContractDocument document, DateTime signedAt, ActorContext actor, CancellationToken ct);
    Task<RentalContractDocument?> GetAsync(int rentalId, CancellationToken ct);
}

public interface IRentalContractDocumentService
{
    Task<Rental> UploadAsync(int rentalId, Stream content, string fileName, string contentType, DateTime signedAt, ActorContext actor, CancellationToken ct);
    Task<RentalContractDocument> DownloadAsync(int rentalId, ActorContext actor, CancellationToken ct);
}

public sealed class RentalContractDocumentService(IRentalBookingService bookings, IRentalContractDocumentRepository documents, TimeProvider clock) : IRentalContractDocumentService
{
    private async Task<Rental> AuthorizeAsync(int rentalId, ActorContext actor, CancellationToken ct)
    {
        var readAll = actor.Role is "Admin" or "Manager" or "CustomerService";
        if (!readAll && actor.Role != "Customer") throw new UnauthorizedAccessException("Contract access is not allowed.");
        if (string.IsNullOrWhiteSpace(actor.Email)) throw new UnauthorizedAccessException("Contract access is not allowed.");
        return await bookings.GetRentalForUserAsync(rentalId, actor.Email, readAll, actor.Role == "Customer", false, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
    }

    public async Task<Rental> UploadAsync(int rentalId, Stream content, string fileName, string contentType, DateTime signedAt, ActorContext actor, CancellationToken ct)
    {
        var rental = await AuthorizeAsync(rentalId, actor, ct);
        if (rental.RentalType != "longterm") throw new ArgumentException("Only long-term rentals require a signed contract document.");
        if (rental.Status is not ("pending" or "pre_inspection"))
            throw new ConflictException("CONTRACT_UPLOAD_CLOSED", "Contract can only be uploaded before the rental starts.");
        if (signedAt.Kind == DateTimeKind.Local) signedAt = signedAt.ToUniversalTime();
        if (signedAt == default || signedAt > clock.GetUtcNow().UtcDateTime)
            throw new ArgumentException("Signing date is required and cannot be in the future.");
        var extension = await ContractUploadValidator.ValidateAsync(content, fileName, contentType, ct);
        using var bytes = new MemoryStream();
        await content.CopyToAsync(bytes, ct);
        await documents.SaveAsync(rentalId, new(bytes.ToArray(), contentType.ToLowerInvariant(), $"signed-contract-{rentalId}{extension}"), signedAt, actor, ct);
        return (await AuthorizeAsync(rentalId, actor, ct));
    }

    public async Task<RentalContractDocument> DownloadAsync(int rentalId, ActorContext actor, CancellationToken ct)
    {
        await AuthorizeAsync(rentalId, actor, ct);
        return await documents.GetAsync(rentalId, ct) ?? throw new NotFoundException("CONTRACT_NOT_FOUND", "Signed contract document was not found.");
    }
}
