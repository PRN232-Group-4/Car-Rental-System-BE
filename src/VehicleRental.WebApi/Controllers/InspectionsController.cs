using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Inspections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/inspections")]
[Authorize(Roles="Admin,Manager,CustomerService,Maintenance")]
public sealed class InspectionsController(IInspectionDirectoryService service):ControllerBase
{
    /// <summary>List all recorded pre/post-rental inspection evidence, including historical vehicles and completed rentals.</summary>
    [HttpGet]
    [ProducesResponseType<OperationsPage<InspectionListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<OperationsPage<InspectionListItem>> List([FromQuery]InspectionListQuery query,CancellationToken ct)=>service.ListAsync(query,ct);
}
