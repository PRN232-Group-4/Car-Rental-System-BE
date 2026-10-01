using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

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
