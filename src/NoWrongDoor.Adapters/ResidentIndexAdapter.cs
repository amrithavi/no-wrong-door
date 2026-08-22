namespace NoWrongDoor.Adapters;

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoWrongDoor.Core.Interfaces;
using NoWrongDoor.Core.Models;
using ResidentRecord = NoWrongDoor.Core.Models.NormalizedResident;

public class ResidentIndexAdapter : IResidentSource
{
    public const string HttpClientName = "ResidentIndex";
    private readonly HttpClient _httpClient;
    private static readonly Uri DefaultBaseUri = new("http://127.0.0.1:8081/");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ResidentIndexAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ResidentIndexAdapter(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
    }

    public async Task<SourceResult<ResidentRecord>> GetByIdAsync(string id)
    {
        try
        {
            var uri = GetUri($"residents/{Uri.EscapeDataString(id)}");
            using var response = await _httpClient.GetAsync(uri).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new SourceResult<ResidentRecord>(SourceStatus.Empty, null, "Record not found");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            ResidentDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ResidentDto>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                return new SourceResult<ResidentRecord>(SourceStatus.Malformed, null, $"JSON deserialization failed: {ex.Message}");
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            {
                return new SourceResult<ResidentRecord>(SourceStatus.Malformed, null, "Response contained empty or invalid resident payload");
            }

            return new SourceResult<ResidentRecord>(SourceStatus.Ok, MapToNormalizedResident(dto));
        }
        catch (HttpRequestException ex)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"Network error: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"Request timed out: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new SourceResult<ResidentRecord>(SourceStatus.Unavailable, null, $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<SourceResult<IReadOnlyList<ResidentRecord>>> SearchAsync(string? name, string? dob)
    {
        var deduplicated = new Dictionary<string, ResidentDto>(StringComparer.OrdinalIgnoreCase);
        int page = 1;
        const int pageSize = 25;

        try
        {
            while (true)
            {
                var uri = GetUri($"residents?page={page}&page_size={pageSize}");
                using var response = await _httpClient.GetAsync(uri).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest)
                {
                    // Out-of-range or client error stops paging
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"HTTP {(int)response.StatusCode} on page {page}");
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                PageDto? pageDto;
                try
                {
                    pageDto = JsonSerializer.Deserialize<PageDto>(content, JsonOptions);
                }
                catch (JsonException ex)
                {
                    return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Malformed, null, $"JSON deserialization failed on page {page}: {ex.Message}");
                }

                if (pageDto == null || pageDto.Results == null)
                {
                    return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Malformed, null, $"Malformed page payload on page {page}");
                }

                foreach (var record in pageDto.Results)
                {
                    if (!string.IsNullOrWhiteSpace(record.Id))
                    {
                        deduplicated.TryAdd(record.Id, record);
                    }
                }

                if (!pageDto.HasMore || pageDto.Results.Count == 0)
                {
                    break;
                }

                page++;
            }

            var normalizedList = deduplicated.Values.Select(MapToNormalizedResident);

            if (!string.IsNullOrWhiteSpace(name))
            {
                var trimmedName = name.Trim();
                normalizedList = normalizedList.Where(r => r.FullName.Contains(trimmedName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(dob))
            {
                var trimmedDob = dob.Trim();
                normalizedList = normalizedList.Where(r => r.DateOfBirth != null && string.Equals(r.DateOfBirth.Trim(), trimmedDob, StringComparison.OrdinalIgnoreCase));
            }

            var results = normalizedList.ToList();
            if (results.Count == 0)
            {
                return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Empty, Array.Empty<ResidentRecord>());
            }

            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Ok, results);
        }
        catch (HttpRequestException ex)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"Network error: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"Request timed out: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new SourceResult<IReadOnlyList<ResidentRecord>>(SourceStatus.Unavailable, null, $"Unexpected error: {ex.Message}");
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

    private static ResidentRecord MapToNormalizedResident(ResidentDto dto)
    {
        var firstName = dto.FirstName ?? string.Empty;
        var lastName = dto.LastName ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();

        return new ResidentRecord(
            Source: "resident_index",
            SourceId: dto.Id ?? string.Empty,
            FullName: fullName,
            DateOfBirth: dto.DateOfBirth,
            AddressLine: dto.AddressLine,
            City: dto.City,
            Phone: dto.Phone,
            ProgramStatus: dto.ProgramStatus,
            LastContact: dto.LastContact,
            BenefitCode: null,
            ReviewDue: null
        );
    }

    private sealed record ResidentDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("date_of_birth")] string? DateOfBirth,
        [property: JsonPropertyName("address_line")] string? AddressLine,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("program_status")] string? ProgramStatus,
        [property: JsonPropertyName("last_contact")] string? LastContact
    );

    private sealed record PageDto(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("page_size")] int PageSize,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("has_more")] bool HasMore,
        [property: JsonPropertyName("results")] List<ResidentDto>? Results
    );
}
