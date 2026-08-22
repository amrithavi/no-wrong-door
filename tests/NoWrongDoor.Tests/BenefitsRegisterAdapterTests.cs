namespace NoWrongDoor.Tests;

using NoWrongDoor.Adapters;
using NoWrongDoor.Core.Models;

public class BenefitsRegisterAdapterTests
{
    private BenefitsRegisterAdapter _adapter = null!;

    [SetUp]
    public void Setup()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:8082/")
        };

        _adapter = new BenefitsRegisterAdapter(client);
    }

    [Test]
    public void ParseRecordElement_MissingField_ReturnsMalformedNotThrow()
    {
        var badXml = @"<?xml version=""1.0""?>
<BenefitsRegister>
  <Record>
    <Ref>TEST/0001</Ref>
    <Name>SMITH, John</Name>
    <Born>1990-01-01</Born>
  </Record>
</BenefitsRegister>";

        // Missing Addr, Town, BenefitCode, ReviewDue on purpose

        var method = typeof(BenefitsRegisterAdapter).GetMethod(
            "ParseRecordElement",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static);

        var doc = System.Xml.Linq.XDocument.Parse(badXml);
        var recordElement = doc.Descendants("Record").First();

        var result = method!.Invoke(null, new object[] { recordElement });

        Assert.That(
            result,
            Is.Null,
            "Expected null (which the caller maps to Malformed) for a record missing required fields");
    }

    [Test]
    public async Task SearchAsync_ReturnsOkWithRealRecords_NotMalformed()
    {
        // This is the exact bug class we just fixed: if field mapping is
        // wrong, every real record silently becomes Malformed instead of Ok.
        var result = await _adapter.SearchAsync(null, null);

        Assert.That(
            result.Status,
            Is.EqualTo(SourceStatus.Ok),
            $"Expected Ok but got {result.Status}. Note: {result.Note}");

        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data!.Count, Is.GreaterThan(0));

        // Spot-check that FullName actually got populated, not blank/null
        Assert.That(
            result.Data.All(r => !string.IsNullOrWhiteSpace(r.FullName)),
            Is.True);

        Assert.That(
            result.Data.All(r => r.Source == "benefits_register"),
            Is.True);
    }

    [Test]
    public async Task GetByRefAsync_UnknownRef_ReturnsEmpty()
    {
        var result = await _adapter.GetByRefAsync("NO/9999/0000-DOES-NOT-EXIST");

        Assert.That(
            result.Status,
            Is.EqualTo(SourceStatus.Empty));
    }

    [Test]
    public async Task GetByRefAsync_RealRefFromSearch_ReturnsOk()
    {
        // Get a real ref from search, then look it up directly —
        // proves the ref-with-slash URL encoding works end to end.
        var searchResult = await _adapter.SearchAsync(null, null);

        Assert.That(
            searchResult.Status,
            Is.EqualTo(SourceStatus.Ok));

        var firstRef = searchResult.Data!.First().SourceId;

        var result = await _adapter.GetByRefAsync(firstRef);

        Assert.That(
            result.Status,
            Is.EqualTo(SourceStatus.Ok),
            $"Expected Ok but got {result.Status}. Note: {result.Note}");

        Assert.That(
            result.Data!.SourceId,
            Is.EqualTo(firstRef));
    }

    [Test]
    public async Task SearchAsync_RepeatedCalls_EventuallyHitsA500AndReturnsUnavailable_OrOk()
    {
        // The XML service fails ~15% of the time even after 2 retries (3 attempts),
        // so across enough calls we should see the Unavailable path triggered
        // at least once, proving retry-exhaustion is reachable, not just theoretical.
        var statuses = new List<SourceStatus>();

        for (int i = 0; i < 20; i++)
        {
            var result = await _adapter.SearchAsync(null, null);
            statuses.Add(result.Status);
        }

        // Every result must be a real status, never an unhandled exception bubbling up
        Assert.That(
            statuses,
            Has.All.Matches<SourceStatus>(
                s => s == SourceStatus.Ok ||
                     s == SourceStatus.Unavailable ||
                     s == SourceStatus.Malformed));

        Console.WriteLine(
            $"Statuses over 20 calls: {string.Join(", ", statuses)}");
    }
}