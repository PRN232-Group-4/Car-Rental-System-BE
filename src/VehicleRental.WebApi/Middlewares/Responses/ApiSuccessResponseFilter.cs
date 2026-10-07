using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Text.RegularExpressions;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Normalizes controller successes and errors, including automatic validation results.
/// </summary>
public sealed class ApiSuccessResponseFilter : IAsyncAlwaysRunResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var statusCode = GetStatusCode(context.Result, context.HttpContext.Response.StatusCode);
        if (statusCode >= 400)
        {
            // Keep Challenge/Forbid results intact so their authentication handlers still run.
            // Empty authentication responses are normalized by status-code pages afterwards.
            switch (context.Result)
            {
                case ObjectResult result when result.Value is not ApiResponse:
                    result.Value = ApiResponse.Failure(statusCode, result.Value);
                    result.DeclaredType = null;
                    result.StatusCode = statusCode;
                    result.ContentTypes.Clear();
                    result.ContentTypes.Add("application/json");
                    break;
                case JsonResult result when result.Value is not ApiResponse:
                    result.Value = ApiResponse.Failure(statusCode, result.Value);
                    result.StatusCode = statusCode;
                    result.ContentType = "application/json";
                    break;
                case StatusCodeResult:
                    context.Result = new ObjectResult(ApiResponse.Failure(statusCode)) { StatusCode = statusCode };
                    break;
            }
            return next();
        }
        if (statusCode is < 200 or >= 300)
            return next();

        var message = GetSuccessMessage(context, statusCode);

        switch (context.Result)
        {
            case ObjectResult objectResult when objectResult.Value is not ApiResponse:
                objectResult.Value = ApiResponse.Create(objectResult.Value, message, statusCode);
                // The envelope now replaces the action's value, so the declared action type no
                // longer matches. Leaving it set makes typed actions (e.g. Task<VehiclePage>)
                // fail with InvalidCastException while serializing the response.
                objectResult.DeclaredType = null;
                break;
            case JsonResult jsonResult when jsonResult.Value is not ApiResponse:
                jsonResult.Value = ApiResponse.Create(jsonResult.Value, message, statusCode);
                break;
            case NoContentResult:
            case StatusCodeResult:
                context.Result = new OkObjectResult(ApiResponse.Create(null, message));
                break;
            case EmptyResult:
                context.Result = new OkObjectResult(ApiResponse.Create(null, message));
                break;
        }

        return next();
    }

    private static int GetStatusCode(IActionResult result, int responseStatusCode) => result switch
    {
        IStatusCodeActionResult { StatusCode: { } statusCode } => statusCode,
        ObjectResult { Value: ProblemDetails { Status: { } statusCode } } => statusCode,
        _ => responseStatusCode
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
        ["Dispatch.CreateShift"] = "Driver duty shift created successfully.",
        ["Dispatch.UpdateShift"] = "Driver duty shift updated successfully.",
        ["Dispatch.Shifts"] = "Driver duty shifts retrieved successfully.",
        ["Dispatch.CreateReport"] = "Incident report submitted for manager review.",
        ["Dispatch.Reports"] = "Incident reports retrieved successfully.",
        ["Dispatch.AssignMaintenance"] = "Maintenance technician assigned to the report successfully.",
        ["Dispatch.ResolveReport"] = "Damage report resolved successfully.",
        ["Dispatch.InspectReplacement"] = "Replacement vehicle inspection recorded successfully.",
        ["Dispatch.ReplaceVehicle"] = "Replacement vehicle dispatched with original rental pricing preserved.",
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
        ["DepartmentMembers.Assign"] = "Employee assigned to department successfully.",
        ["FacilityMembers.Assign"] = "Employee assigned to facility successfully.",
        ["Operations.AddDepartment"] = "Department created successfully.",
        ["Operations.EditDepartment"] = "Department updated successfully.",
        ["Operations.DeleteDepartment"] = "Department deleted successfully.",
        ["Operations.Facilities"] = "Facilities retrieved successfully.",
        ["Operations.AddFacility"] = "Facility created successfully.",
        ["Operations.EditFacility"] = "Facility updated successfully.",
        ["Operations.DeleteFacility"] = "Facility deleted successfully.",
        ["Operations.Policies"] = "Rental policies retrieved successfully.",
        ["Operations.AddPolicy"] = "Rental policy created successfully.",
        ["Operations.Hourly"] = "Hourly rental created successfully.",
        ["Operations.Longterm"] = "Long-term rental created successfully.",
        ["Operations.SaveContract"] = "Signed long-term rental contract recorded successfully.",
        ["Operations.Rental"] = "Rental retrieved successfully.",
        ["Operations.Assign"] = "Driver assignment updated successfully.",
        ["Operations.DriverSchedule"] = "Driver schedule retrieved successfully.",
        ["Operations.Pre"] = "Pre-rental inspection recorded successfully.",
        ["Operations.Post"] = "Post-rental inspection recorded successfully.",
        ["Operations.Status"] = "Rental status updated successfully.",
        ["Operations.Plans"] = "Subscription plans retrieved successfully.",
        ["Operations.Plan"] = "Subscription plan retrieved successfully.",
        ["Operations.AddPlan"] = "Subscription plan created successfully.",
        ["Operations.EditPlan"] = "Subscription plan updated successfully.",
        ["Operations.DeletePlan"] = "Subscription plan deleted successfully.",
        ["Operations.CustomerSubscriptions"] = "Customer subscriptions retrieved successfully.",
        ["Operations.AddMaintenance"] = "Maintenance record created successfully.",
        ["Operations.EditMaintenance"] = "Maintenance record updated successfully.",
        ["Operations.Dashboard"] = "Dashboard data retrieved successfully.",
        ["Payments.Checkout"] = "Checkout link created successfully.",
        ["Payments.PurchasePlan"] = "Subscription plan checkout created successfully.",
        ["Payments.PlanPurchase"] = "Subscription purchase retrieved successfully.",
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
