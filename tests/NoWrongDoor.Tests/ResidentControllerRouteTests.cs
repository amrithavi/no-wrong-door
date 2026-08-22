namespace NoWrongDoor.Tests;

using System.Net.Http.Json;
using System.Text.Json;

public class ResidentControllerRouteTests
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://localhost:5220/") };

    [Test]
    public async Task GetResident_BenefitsRefWithSlashes_ReturnsOk()
    {
        // Regression test for the {id} vs {*id} route bug: a plain {id}
        // segment silently truncates refs like "AS/2024/4702" at the first
        // slash, so this must be run against the live, routed HTTP endpoint,
        // not the adapter directly, or it won't catch a route regression.
        var response = await Client.GetAsync("resident/benefits_register/AS/2024/4702");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("Ok"),
            "A slash-containing ref must resolve correctly — if this returns Empty, the route lost its catch-all and is truncating the ref again.");
    }
}