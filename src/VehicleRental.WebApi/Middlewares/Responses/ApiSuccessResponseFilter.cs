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
        ["NavigationCounts.Get"] = "Pending navigation queue counts retrieved successfully.",
        ["InspectionAssignments.Get"] = "Pre-rental inspection assignment retrieved successfully.",
        ["InspectionAssignments.Assign"] = "Pre-rental inspection technician assigned successfully.",
        ["InspectionAssignments.ConfirmChecked"] = "Technician dispatch and passing pre-rental check confirmed successfully.",
        ["Staff.CreateFromUser"] = "Employee profile created for the selected account successfully.",
        ["Inspections.List"] = "Recorded vehicle inspections retrieved successfully.",
        ["Users.List"] = "Users retrieved successfully.",
        ["Users.Get"] = "User retrieved successfully.",
        ["Users.SetStatus"] = "User account status updated successfully.",
        ["Users.AddRole"] = "User role assigned successfully.",
        ["Users.RemoveRole"] = "User role removed successfully.",
        ["Users.RevokeSessions"] = "User sessions revoked successfully.",
        ["Dashboard.Snapshot"] = "Dashboard snapshot retrieved successfully.",
        ["Dashboard.RevenueSeries"] = "Daily revenue series retrieved successfully.",
        ["Dashboard.TopVehicles"] = "Vehicle performance ranking retrieved successfully.",
        ["Dashboard.MaintenanceRevenue"] = "Vehicle maintenance costs and revenue retrieved successfully.",
        ["Dashboard.PlanUsage"] = "Subscription plan usage retrieved successfully.",
        ["Operations.ModerateRating"] = "Staff rating moderation status updated successfully.",
        ["Operations.ReplyRating"] = "Management reply to staff rating saved successfully.",
        ["Dispatch.SetReportStatus"] = "Incident report status updated successfully.",
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
        ["Account.StartPasswordChange"] = "Password change OTP sent successfully.",
        ["ExternalAuth.GoogleComplete"] = "Google sign-in completed successfully.",
        ["Home.GetStatus"] = "Service status retrieved successfully.",
        ["Media.UploadAvatar"] = "Avatar uploaded successfully.",
        ["Media.UploadVehicleImage"] = "Vehicle image uploaded successfully.",
        ["Media.UploadStaffLicense"] = "Staff driver license image uploaded successfully.",
        ["Media.UploadInspectionImage"] = "Inspection image uploaded successfully.",
        ["Media.UploadReportImage"] = "Incident report image uploaded successfully.",
        ["Customers.List"] = "Customers retrieved successfully.",
        ["AuditLogs.List"] = "Audit logs retrieved successfully.",
        ["Roles.List"] = "Roles retrieved successfully.",
        ["Roles.Get"] = "Role retrieved successfully.",
        ["Roles.Create"] = "Role created successfully.",
        ["Roles.Update"] = "Role updated successfully.",
        ["Roles.Delete"] = "Role deleted successfully.",
        ["Customers.Get"] = "Customer retrieved successfully.",
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
        ["Operations.SaveLicenseUrl"] = "Driver license saved successfully.",
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
        ["Payments.ConfirmCash"] = "Cash payment confirmed successfully.",
        ["Payments.StaffCheckout"] = "Overtime payment link created successfully.",
        ["VehicleSearch.Search"] = "Vehicles retrieved successfully.",
        ["VehicleSearch.Availability"] = "Vehicle availability retrieved successfully.",
        ["VehicleSearch.BusySlots"] = "Vehicle busy slots retrieved successfully.",
        ["VehicleSearch.ActivePolicy"] = "Active rental policy retrieved successfully.",
        ["VehicleSearch.AffectedRentals"] = "Affected rentals retrieved successfully.",
        ["Operations.AddFacility"] = "Facility created successfully.",
        ["Operations.EditFacility"] = "Facility updated successfully.",
        ["Operations.DeleteFacility"] = "Facility deleted successfully.",
        ["Operations.Policies"] = "Rental policies retrieved successfully.",
        ["Operations.AddPolicy"] = "Rental policy created successfully.",
        ["Operations.SetPolicyStatus"] = "Rental policy status updated successfully.",
        ["Operations.Hourly"] = "Hourly rental created successfully.",
        ["Operations.Longterm"] = "Long-term rental created successfully.",
        ["Operations.SaveContract"] = "Signed long-term rental contract recorded successfully.",
        ["Operations.Rental"] = "Rental retrieved successfully.",
        ["RentalEditing.Update"] = "Rental updated successfully.",
        ["RentalQueries.List"] = "Rentals retrieved successfully.",
        ["RentalQueries.Mine"] = "Your rentals retrieved successfully.",
        ["RentalQueries.Detail"] = "Rental details retrieved successfully.",
        ["RentalQueries.MineDetail"] = "Your rental details retrieved successfully.",
        ["RentalQueries.Overdue"] = "Overdue rentals retrieved successfully.",
        ["RentalLifecycle.Confirm"] = "Rental confirmed successfully.",
        ["RentalLifecycle.Start"] = "Rental started successfully.",
        ["RentalLifecycle.Return"] = "Vehicle return recorded successfully.",
        ["RentalLifecycle.Complete"] = "Rental completed successfully.",
        ["RentalLifecycle.Cancel"] = "Rental cancelled successfully.",
        ["RentalLifecycle.CancellationPreview"] = "Rental cancellation preview retrieved successfully.",
        ["Handovers.List"] = "Vehicle handovers retrieved successfully.",
        ["Handovers.Detail"] = "Vehicle handover retrieved successfully.",
        ["Handovers.Confirm"] = "Vehicle handover confirmed successfully.",
        ["RentalAssignments.AssignDriver"] = "Driver assigned successfully.",
        ["RentalAssignments.ReplaceVehicle"] = "Vehicle replaced successfully.",
        ["RentalAssignments.ListAssignments"] = "Rental assignments retrieved successfully.",
        ["RentalAssignments.ListAssignableDrivers"] = "Assignable drivers retrieved successfully.",
        ["RentalAssignments.ListAssignableVehicles"] = "Assignable vehicles retrieved successfully.",
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
        ["Payments.RejectRefund"] = "Refund rejected successfully.",
        ["Tracking.Ingest"] = "GPS telemetry coordinates ingested successfully.",
        ["Tracking.LastPosition"] = "Vehicle last position retrieved successfully.",
        ["Tracking.History"] = "Vehicle location history retrieved successfully.",
        ["Tracking.Nearby"] = "Nearby vehicles retrieved successfully.",
        ["Tracking.Route"] = "Rental route retrieved successfully.",
        ["Tracking.VerifyDropoff"] = "Drop-off location verification completed successfully.",
        ["Tracking.PurgeRetention"] = "Expired GPS telemetry purged successfully.",
        ["Tracking.Alerts"] = "Tracking alerts retrieved successfully.",
        ["Tracking.ResolveAlert"] = "Tracking alert resolved successfully.",
        ["Map.Geocode"] = "Address geocoded successfully.",
        ["Map.ReverseGeocode"] = "Coordinates reverse-geocoded successfully.",
        ["Map.Places"] = "Places retrieved successfully.",
        ["Map.DistanceMatrix"] = "Distance and ETA calculated successfully.",
        ["Map.Directions"] = "Directions calculated successfully.",
        ["Devices.List"] = "Tracking devices retrieved successfully.",
        ["Devices.Register"] = "Tracking device registered successfully.",
        ["Devices.Get"] = "Tracking device retrieved successfully.",
        ["Devices.Update"] = "Tracking device updated successfully.",
        ["Geofences.List"] = "Geofences retrieved successfully.",
        ["Geofences.Create"] = "Geofence created successfully.",
        ["Geofences.Events"] = "Geofence events retrieved successfully."
    };
}
