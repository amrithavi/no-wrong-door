namespace NoWrongDoor.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using NoWrongDoor.Core.Models;

public class ResidentControllerRouteTests
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://localhost:5220/") };

    [Test]
    public async Task GetResident_BenefitsRefWithSlashes_RoutesCorrectly()
    {
        // Regression test for the {id} vs {*id} route bug: a plain {id}
        // segment silently truncates refs like "AS/2024/4702" at the first
        // slash. Must run against the live, routed HTTP endpoint, not the
        // adapter directly, or it won't catch a route regression.
        //
        // At the current 40% Benefits Register failure rate, a live call
        // can legitimately come back Unavailable after retries exhaust —
        // that's not a routing failure. What this test must NOT tolerate
        // is Empty, which is what the route bug actually produced: the
        // ref gets truncated, the (wrong, partial) ref is looked up, and
        // the service correctly reports no match for a ref that isn't real.
        var response = await Client.GetAsync("resident/benefits_register/AS/2024/4702");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var status = body.GetProperty("status").GetString();

        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(status, Is.Not.EqualTo("Empty"),
            "Empty means the ref was truncated by routing and looked up wrong — the exact route bug this test guards against.");
        Assert.That(status, Is.EqualTo("Ok").Or.EqualTo("Unavailable"),
            $"Expected Ok or Unavailable (transient failure), got {status}.");

        if (status == "Ok")
        {
            var sourceId = body.GetProperty("data").GetProperty("sourceId").GetString();
            Assert.That(sourceId, Is.EqualTo("AS/2024/4702"),
                "Ok response returned the wrong record — ref may have been altered in transit.");
        }
    }
}