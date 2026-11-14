using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("[controller]")]
public class HomeController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Index()
    {
        return Redirect("/swagger");
    }

    [SuccessMessage("Service status retrieved successfully.")]
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Service = "Car Rental System API",
            Status = "Running",
            Timestamp = DateTime.UtcNow
        });
    }
}
