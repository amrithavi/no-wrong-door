namespace NoWrongDoor.Tests;

using NoWrongDoor.Adapters;

public class ResidentIndexAdapterTests
{
    private ResidentIndexAdapter _adapter = null!;

    [SetUp]
    public void Setup()
    {
        var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8081/") };
        _adapter = new ResidentIndexAdapter(client);
    }

    [Test]
    public async Task SearchAsync_ReturnsNoDuplicateSourceIds()
    {
        var result = await _adapter.SearchAsync(null, null);

        Assert.That(result.Status, Is.EqualTo(NoWrongDoor.Core.Models.SourceStatus.Ok));

        var ids = result.Data!.Select(r => r.SourceId).ToList();
        var distinctIds = ids.Distinct().ToList();

        Assert.That(ids.Count, Is.EqualTo(distinctIds.Count),
            "SearchAsync returned duplicate SourceIds — pagination dedup failed");
    }

    [Test]
    public async Task SearchAsync_ContainsBothKnownBoundaryDuplicateIds()
    {
        var result = await _adapter.SearchAsync(null, null);
        var ids = result.Data!.Select(r => r.SourceId).ToHashSet();

        Assert.That(ids, Does.Contain("R-10594"));
        Assert.That(ids, Does.Contain("R-10057"));
    }

    [Test]
    public async Task GetByIdAsync_UnknownId_ReturnsEmpty()
    {
        var result = await _adapter.GetByIdAsync("R-99999-DOES-NOT-EXIST");
        Assert.That(result.Status, Is.EqualTo(NoWrongDoor.Core.Models.SourceStatus.Empty));
    }
}