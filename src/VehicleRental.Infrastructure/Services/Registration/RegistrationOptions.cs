using Car_Rental_System_BE.Application.DTOs.Operations;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Infrastructure.Services.Registration;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";
    [Required] public string ApiKey { get; init; } = null!;
    [Required, EmailAddress] public string SenderEmail { get; init; } = null!;
    [Required] public string SenderName { get; init; } = "Vehicle Rental";
}
