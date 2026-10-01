using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Text.RegularExpressions;

namespace Car_Rental_System_BE.Responses;

/// <summary>
/// Wraps every successful controller response in <see cref="ApiSuccessResponse"/>.
/// </summary>
public sealed class ApiSuccessResponseFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var statusCode = GetStatusCode(context.Result);
        if (statusCode is < 200 or >= 300)
            return next();

        var message = GetSuccessMessage(context, statusCode);

        switch (context.Result)
        {
            case ObjectResult objectResult:
                objectResult.Value = ApiSuccessResponse.Create(objectResult.Value, message);
                break;
            case JsonResult jsonResult:
                jsonResult.Value = ApiSuccessResponse.Create(jsonResult.Value, message);
                break;
            case NoContentResult:
            case StatusCodeResult:
                context.Result = new OkObjectResult(ApiSuccessResponse.Create(null, message));
                break;
            case EmptyResult:
                context.Result = new OkObjectResult(ApiSuccessResponse.Create(null, message));
                break;
        }

        return next();
    }

    private static int GetStatusCode(IActionResult result) => result switch
    {
        IStatusCodeActionResult { StatusCode: { } statusCode } => statusCode,
        _ => StatusCodes.Status200OK
    };

    private static string GetSuccessMessage(ResultExecutingContext context, int statusCode)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action)
            return "Request completed successfully.";

        var key = $"{action.ControllerName}.{action.ActionName}";
        if (Messages.TryGetValue(key, out var message))
            return message;

        var operation = Regex.Replace(action.ActionName, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
        return statusCode switch
        {
            StatusCodes.Status201Created => $"{operation} created successfully.",
            StatusCodes.Status202Accepted => $"{operation} accepted successfully.",
            _ when HttpMethods.IsGet(context.HttpContext.Request.Method) => $"{operation} retrieved successfully.",
            _ when HttpMethods.IsDelete(context.HttpContext.Request.Method) => $"{operation} deleted successfully.",
            _ when HttpMethods.IsPut(context.HttpContext.Request.Method) || HttpMethods.IsPatch(context.HttpContext.Request.Method) => $"{operation} updated successfully.",
            _ => $"{operation} completed successfully."
        };
    }

    // Keep endpoint-specific wording here so every successful response has a meaningful,
    // stable message without controllers repeating envelope construction.
    private static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>
    {
        ["Account.Start"] = "Registration OTP sent successfully.",
        ["Account.Verify"] = "Registration verified successfully.",
        ["Account.Resend"] = "Registration OTP resent successfully.",
        ["Account.StartPasswordReset"] = "Password reset OTP sent successfully.",
        ["Account.CompletePasswordReset"] = "Password reset completed successfully.",
        ["Account.GetProfile"] = "Profile retrieved successfully.",
        ["Account.UpdateProfile"] = "Profile updated successfully.",
        ["Account.ChangePassword"] = "Password changed successfully.",
        ["ExternalAuth.GoogleComplete"] = "Google sign-in completed successfully.",
        ["Home.GetStatus"] = "Service status retrieved successfully.",
        ["Media.UploadAvatar"] = "Avatar uploaded successfully.",
        ["Session.Login"] = "Signed in successfully.",
        ["Session.Logout"] = "Signed out successfully.",
        ["Session.LogoutAll"] = "Signed out from all sessions successfully.",
        ["Staff.List"] = "Staff list retrieved successfully.",
        ["Staff.Get"] = "Staff member retrieved successfully.",
        ["Staff.ListDrivers"] = "Driver list retrieved successfully.",
        ["Staff.Create"] = "Staff member created successfully.",
        ["Staff.Update"] = "Staff member updated successfully.",
        ["Staff.Deactivate"] = "Staff member deactivated successfully.",
        ["Operations.License"] = "Driver license retrieved successfully.",
        ["Operations.SaveLicense"] = "Driver license saved successfully.",
        ["Operations.ReviewLicense"] = "Driver license review completed successfully.",
        ["Operations.VehicleTypes"] = "Vehicle types retrieved successfully.",
        ["Operations.AddType"] = "Vehicle type created successfully.",
        ["Operations.EditType"] = "Vehicle type updated successfully.",
        ["Operations.DeleteType"] = "Vehicle type deleted successfully.",
        ["Operations.Vehicles"] = "Vehicles retrieved successfully.",
        ["Operations.Vehicle"] = "Vehicle retrieved successfully.",
        ["Operations.AddVehicle"] = "Vehicle created successfully.",
        ["Operations.EditVehicle"] = "Vehicle updated successfully.",
        ["Operations.Departments"] = "Departments retrieved successfully.",
        ["Operations.AddDepartment"] = "Department created successfully.",
        ["Operations.EditDepartment"] = "Department updated successfully.",
        ["Operations.DeleteDepartment"] = "Department deleted successfully.",
        ["Operations.Facilities"] = "Facilities retrieved successfully.",
        ["Operations.AddFacility"] = "Facility created successfully.",
        ["Operations.EditFacility"] = "Facility updated successfully.",
        ["Operations.DeleteFacility"] = "Facility deleted successfully.",
        ["Operations.Policies"] = "Rental policies retrieved successfully.",
        ["Operations.AddPolicy"] = "Rental policy created successfully.",
        ["Operations.EditPolicy"] = "Rental policy updated successfully.",
        ["Operations.Hourly"] = "Hourly rental created successfully.",
        ["Operations.Longterm"] = "Long-term rental created successfully.",
        ["Operations.Rental"] = "Rental retrieved successfully.",
        ["Operations.Assign"] = "Driver assignment updated successfully.",
        ["Operations.Pre"] = "Pre-rental inspection recorded successfully.",
        ["Operations.Post"] = "Post-rental inspection recorded successfully.",
        ["Operations.Status"] = "Rental status updated successfully.",
        ["Operations.Plans"] = "Subscription plans retrieved successfully.",
        ["Operations.Plan"] = "Subscription plan retrieved successfully.",
        ["Operations.AddPlan"] = "Subscription plan created successfully.",
        ["Operations.EditPlan"] = "Subscription plan updated successfully.",
        ["Operations.DeletePlan"] = "Subscription plan deleted successfully.",
        ["Operations.Purchase"] = "Subscription plan purchased successfully.",
        ["Operations.AddMaintenance"] = "Maintenance record created successfully.",
        ["Operations.EditMaintenance"] = "Maintenance record updated successfully.",
        ["Operations.Dashboard"] = "Dashboard data retrieved successfully.",
        ["Payments.Checkout"] = "Checkout link created successfully.",
        ["Payments.Webhook"] = "Payment webhook processed successfully.",
        ["Payments.Refund"] = "Refund request submitted successfully.",
        ["Payments.Invoices"] = "Invoices retrieved successfully.",
        ["Payments.Invoice"] = "Invoice retrieved successfully.",
        ["Payments.CreateInvoice"] = "Invoice created successfully.",
        ["Payments.Payments"] = "Payments retrieved successfully.",
        ["Payments.ApproveRefund"] = "Refund approved successfully.",
        ["Payments.RejectRefund"] = "Refund rejected successfully."
    };
}
