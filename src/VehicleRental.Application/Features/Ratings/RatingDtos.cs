using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Ratings;

public sealed record StaffRating(int Id, int RentalId, int StaffId, decimal Rating, string? Comment, DateTime CreatedAt, string? CustomerName = null, string Status = "visible", RatingReply? Reply = null, string? VehicleName = null, string? LicensePlate = null);
public sealed record RatingReply(string Text, string By, DateTime At);
public sealed record RatingModerationRequest([param: Required, RegularExpression("^(visible|hidden|reported)$")] string Status,
    [param: Required, StringLength(2000, MinimumLength=1)] string Reason);
public sealed record RatingReplyRequest([param: Required, StringLength(4000, MinimumLength=1)] string Text);
public sealed record RateStaffRequest([param:Range(1,int.MaxValue)]int StaffId,[param:Range(1,5)]decimal Rating,[param:StringLength(500)]string? Comment);
