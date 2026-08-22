namespace NoWrongDoor.Adapters;

using System.Net;
using System.Xml;
using System.Xml.Linq;
using Polly;
using Polly.Timeout;
using NoWrongDoor.Core.Interfaces;
using NoWrongDoor.Core.Models;
using ResidentRecord = NoWrongDoor.Core.Models.NormalizedResident;

public class BenefitsRegisterAdapter : IBenefitsSource
{
    public const string HttpClientName = "BenefitsRegister";
    private readonly HttpClient _httpClient;
    private static readonly Uri DefaultBaseUri = new("http://127.0.0.1:8082/");

    private static readonly IAsyncPolicy<HttpResponseMessage> RetryPolicy = Policy
        .HandleResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.InternalServerError)
        .RetryAsync(2);

    private static readonly IAsyncPolicy<HttpResponseMessage> TimeoutPolicy = Policy
        .TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(3));

    private static readonly IAsyncPolicy<HttpResponseMessage> PolicyWrap = Policy.WrapAsync(RetryPolicy, TimeoutPolicy);

    public BenefitsRegisterAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public BenefitsRegisterAdapter(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
    }

    public async Task<SourceResult<ResidentRecord>> GetByRefAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Empty, null, "Reference cannot be empty");
        }

        var encodedRef = Uri.EscapeDataString(reference.Trim());
        var (response, attempts, exception) = await SendRequestAsync($"records/{encodedRef}").ConfigureAwait(false);

        if (exception != null)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"Request failed ({exception.GetType().Name}): {exception.Message}");
        }

        if (response == null)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, "No response received from Benefits Register");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Empty, null, "Record not found");
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"Persistent HTTP 500: retries exhausted after {attempts} attempts.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        string content;
        try
        {
            content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"Failed reading response content: {ex.Message}");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Malformed, null, $"Malformed XML: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Malformed, null, $"Failed to parse XML: {ex.Message}");
        }

        var recordElement = doc.Descendants("Record").FirstOrDefault();
        if (recordElement == null)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Malformed, null, "Missing <Record> element in XML response.");
        }

        var record = ParseRecordElement(recordElement);
        if (record == null)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Malformed, null, "Record is missing one or more required XML fields.");
        }

        return new SourceResult<ResidentRecord>(SourceStatus.Ok, record);
    }

    public async Task<SourceResult<IReadOnlyList<ResidentRecord>>> SearchAsync(string? name, string? dob)
    {
        var (response, attempts, exception) = await SendRequestAsync("records").ConfigureAwait(false);

        if (exception != null)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"Request failed ({exception.GetType().Name}): {exception.Message}");
        }

        if (response == null)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, "No response received from Benefits Register");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Empty, Array.Empty<ResidentRecord>(), "Records endpoint not found");
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"Persistent HTTP 500: retries exhausted after {attempts} attempts.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        string content;
        try
        {
            content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"Failed reading response content: {ex.Message}");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Malformed, null, $"Malformed XML: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Malformed, null, $"Failed to parse XML: {ex.Message}");
        }

        var recordElements = doc.Descendants("Record").ToList();
        if (recordElements.Count == 0 && doc.Root?.Name.LocalName != "BenefitsRegister")
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Malformed, null, "Invalid XML root or missing <BenefitsRegister>.");
        }

        var list = new List<ResidentRecord>();
        foreach (var elem in recordElements)
        {
            var resident = ParseRecordElement(elem);
            if (resident == null)
            {
                return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Malformed, null, "One or more XML records are missing required fields.");
            }
            list.Add(resident);
        }

        var filtered = list.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var trimmedName = name.Trim();
            filtered = filtered.Where(r => r.FullName.Contains(trimmedName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(dob))
        {
            var trimmedDob = dob.Trim();
            filtered = filtered.Where(r => r.DateOfBirth != null && string.Equals(r.DateOfBirth.Trim(), trimmedDob, StringComparison.OrdinalIgnoreCase));
        }

        var results = filtered.ToList();
        if (results.Count == 0)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Empty, Array.Empty<ResidentRecord>());
        }

        return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Ok, results);
    }

    private async Task<(HttpResponseMessage? Response, int Attempts, Exception? Exception)> SendRequestAsync(string relativePath)
    {
        var context = new Context();
        context["attempts"] = 0;

        try
        {
            var response = await PolicyWrap.ExecuteAsync(async (ctx, ct) =>
            {
                var count = ctx.TryGetValue("attempts", out var val) && val is int c ? c : 0;
                ctx["attempts"] = count + 1;

                var uri = GetUri(relativePath);
                return await _httpClient.GetAsync(uri, ct).ConfigureAwait(false);
            }, context, CancellationToken.None).ConfigureAwait(false);

            int attempts = context.TryGetValue("attempts", out var val) && val is int c ? c : 1;
            return (response, attempts, null);
        }
        catch (Exception ex)
        {
            int attempts = context.TryGetValue("attempts", out var val) && val is int c ? c : 1;
            return (null, attempts, ex);
        }
    }

    private Uri GetUri(string relativePath)
    {
        if (_httpClient.BaseAddress != null)
        {
            return new Uri(_httpClient.BaseAddress, relativePath);
        }

        return new Uri(DefaultBaseUri, relativePath);
    }

    private static ResidentRecord? ParseRecordElement(XElement record)
    {
        var refElem = record.Element("Ref");
        var nameElem = record.Element("Name");
        var bornElem = record.Element("Born");
        var addrElem = record.Element("Addr");
        var townElem = record.Element("Town");
        var benefitCodeElem = record.Element("BenefitCode");
        var reviewDueElem = record.Element("ReviewDue");

        if (refElem == null || nameElem == null || bornElem == null || addrElem == null ||
            townElem == null || benefitCodeElem == null || reviewDueElem == null)
        {
            return null;
        }

        var sourceId = refElem.Value.Trim();
        var rawName = nameElem.Value;
        var fullName = ParseXmlName(rawName);

        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        return new ResidentRecord(
            Source: "benefits_register",
            SourceId: sourceId,
            FullName: fullName,
            DateOfBirth: bornElem.Value.Trim(),
            AddressLine: addrElem.Value.Trim(),
            City: townElem.Value.Trim(),
            Phone: null,
            ProgramStatus: null,
            LastContact: null,
            BenefitCode: benefitCodeElem.Value.Trim(),
            ReviewDue: reviewDueElem.Value.Trim()
        );
    }

    private static string ParseXmlName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var commaIndex = rawName.IndexOf(',');
        if (commaIndex >= 0)
        {
            var lastName = rawName[..commaIndex].Trim();
            var firstName = rawName[(commaIndex + 1)..].Trim();
            return $"{firstName} {lastName}".Trim();
        }

        return rawName.Trim();
    }
}
