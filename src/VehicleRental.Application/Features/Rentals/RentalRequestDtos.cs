using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Rentals;

public sealed record RentalRequest([param:Range(1,int.MaxValue)]int VehicleId,int? PolicyId,DateTime StartTime,DateTime EndTime,int? SubscriptionId,bool HasDriver=false,[param:RegularExpression("^(payos|cash)$")]string PaymentMethod="payos");
public sealed record RentalContractRequest([param:Required,Url,StringLength(255)]string ContractLink, DateTime SignedAt);
