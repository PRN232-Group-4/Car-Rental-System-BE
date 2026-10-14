using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.External;

public interface IAccessTokenIssuer
{
    TimeSpan AccessTokenLifetime { get; }
    string CreateAccessToken(Account account, string sessionId);
}
