namespace NoWrongDoor.Api.Controllers;

using Microsoft.AspNetCore.Mvc;

[ApiController]
public class ResidentController : ControllerBase
{
    [HttpGet("resident/{source}/{id}")]
    public Task<IActionResult> GetResident(string source, string id)
    {
        throw new NotImplementedException();
    }

    [HttpGet("residents/search")]
    public Task<IActionResult> SearchResidents([FromQuery] string? name, [FromQuery] string? dob)
    {
        throw new NotImplementedException();
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        throw new NotImplementedException();
    }
}
