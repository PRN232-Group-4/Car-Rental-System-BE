using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

/// <summary>
/// Records the signature of a long-term contract on the rental. The action used to sit on
/// <c>OperationsController</c> (SaveContract); route, verb, policy, route constraints, status codes, request and
/// response shapes, success message and swagger description travelled with it unchanged.
/// </summary>
/// <remarks>
/// A controller of its own rather than an action on <c>RentalContractDocumentsController</c>: that type carries
/// a class-level route prefix (<c>api/rental/{id}/contract</c>) and a class-level role list for the uploaded
/// document, and ASP.NET Core ANDs class-level with action-level policy metadata. This route and its
/// CustomerService policy stay exactly as they were.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class RentalContractsController(IRentalEditingService editing) : ControllerBase
{
    /// <summary>Admin, Manager or CustomerService records signed long-term contract evidence.</summary>
    [SuccessMessage("Signed long-term rental contract recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)]
    [HttpPut("rental/{id:int}/contract")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveContract(int id, RentalContractRequest request, CancellationToken ct) =>
        Ok(await editing.SaveRentalContractAsync(id, request.ContractLink, request.SignedAt, ct));
}
