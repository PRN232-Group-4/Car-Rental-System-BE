using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Inspections;

[ApiController]
[Route("api/inspections")]
[Authorize(Roles="Admin,Manager,CustomerService,Maintenance")]
public sealed class InspectionsController(IInspectionDirectoryService service):ControllerBase
{
    /// <summary>Liệt kê mọi bằng chứng kiểm tra trước/sau thuê đã ghi, gồm cả xe cũ và đơn thuê đã hoàn tất.</summary>
    [OperationDescription("Admin/Manager/CustomerService/Maintenance only. Lists all persisted pre/post-rental inspections including previous vehicles and closed rentals, with recorded inspectorEmail, vehicleName/licensePlate, evidence/imageUrls and time. Optional inspectionType pre_rental/post_rental and positive rentalId combine before counting. Pagination defaults page=1/pageSize=20; page>=1, pageSize 1..100, invalid input returns 400 at HTTP/Application. data has items/page/pageSize/totalCount, out-of-range pages keep count with empty items. Stable order inspectedAt DESC NULLS LAST then id DESC. Pending work comes from rental operations queues, never fabricated inspection rows.")]
    [OperationDescription(" confirmationSource distinguishes technician evidence, driver_default automatic confirmation and admin_confirmation; confirmedBy records the confirming actor. Automatic/manual confirmations do not fabricate odometer, fuel or image evidence.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Recorded vehicle inspections retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<OperationsPage<InspectionListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<OperationsPage<InspectionListItem>> List([FromQuery]InspectionListQuery query,CancellationToken ct)=>service.ListAsync(query,ct);
}
