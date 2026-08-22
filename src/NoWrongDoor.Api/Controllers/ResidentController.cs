namespace NoWrongDoor.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using NoWrongDoor.Adapters;
using NoWrongDoor.Core.Interfaces;
using NoWrongDoor.Core.Models;
using ResidentRecord = NoWrongDoor.Core.Models.NormalizedResident;

[ApiController]
public class ResidentController : ControllerBase
{
    private readonly IResidentSource _residentSource;
    private readonly IBenefitsSource _benefitsSource;
    private readonly IHttpClientFactory _httpClientFactory;

    public ResidentController(
        IResidentSource residentSource,
        IBenefitsSource benefitsSource,
        IHttpClientFactory httpClientFactory)
    {
        _residentSource = residentSource;
        _benefitsSource = benefitsSource;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("resident/{source}/{*id}")]
    public async Task<IActionResult> GetResident(string source, string id)
    {
        if (string.Equals(source, "resident_index", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _residentSource.GetByIdAsync(id).ConfigureAwait(false);
            return Ok(result);
        }

        if (string.Equals(source, "benefits_register", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _benefitsSource.GetByRefAsync(id).ConfigureAwait(false);
            return Ok(result);
        }

        return BadRequest(new
        {
            error = "invalid_source",
            message = $"Invalid source '{source}'. Valid sources are 'resident_index' and 'benefits_register'."
        });
    }

    [HttpGet("residents/search")]
    public async Task<IActionResult> SearchResidents([FromQuery] string? name, [FromQuery] string? dob)
    {
        var residentTask = _residentSource.SearchAsync(name, dob);
        var benefitsTask = _benefitsSource.SearchAsync(name, dob);

        await Task.WhenAll(residentTask, benefitsTask).ConfigureAwait(false);

        var residentResult = await residentTask;
        var benefitsResult = await benefitsTask;

        var candidates = new List<ResidentRecord>();
        if (residentResult.Data != null)
        {
            candidates.AddRange(residentResult.Data);
        }
        if (benefitsResult.Data != null)
        {
            candidates.AddRange(benefitsResult.Data);
        }

        var response = new
        {
            candidates,
            sources_status = new Dictionary<string, object?>
            {
                ["resident_index"] = new
                {
                    status = residentResult.Status.ToString().ToLowerInvariant(),
                    note = residentResult.Note
                },
                ["benefits_register"] = new
                {
                    status = benefitsResult.Status.ToString().ToLowerInvariant(),
                    note = benefitsResult.Note
                }
            }
        };

        return Ok(response);
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var residentClient = _httpClientFactory.CreateClient(ResidentIndexAdapter.HttpClientName);
        var benefitsClient = _httpClientFactory.CreateClient(BenefitsRegisterAdapter.HttpClientName);

        var residentHealthTask = CheckServiceHealthAsync(residentClient, "http://127.0.0.1:8081/health");
        var benefitsHealthTask = CheckServiceHealthAsync(benefitsClient, "http://127.0.0.1:8082/health");

        await Task.WhenAll(residentHealthTask, benefitsHealthTask).ConfigureAwait(false);

        var (residentOk, residentStatus, residentBody) = await residentHealthTask;
        var (benefitsOk, benefitsStatus, benefitsBody) = await benefitsHealthTask;

        var overallStatus = (residentOk && benefitsOk) ? "healthy" : "degraded";

        return Ok(new
        {
            status = overallStatus,
            sources = new
            {
                resident_index = new
                {
                    healthy = residentOk,
                    status = residentStatus,
                    response = residentBody
                },
                benefits_register = new
                {
                    healthy = benefitsOk,
                    status = benefitsStatus,
                    response = benefitsBody
                }
            }
        });
    }

    private static async Task<(bool isOk, string status, string? responseBody)> CheckServiceHealthAsync(
        HttpClient client,
        string defaultUrl)
    {
        try
        {
            var uri = client.BaseAddress != null ? new Uri(client.BaseAddress, "health") : new Uri(defaultUrl);
            using var response = await client.GetAsync(uri).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return (true, "ok", content);
            }

            return (false, $"HTTP {(int)response.StatusCode}", content);
        }
        catch (Exception ex)
        {
            return (false, "unavailable", ex.Message);
        }
    }
}
