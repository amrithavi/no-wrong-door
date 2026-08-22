namespace NoWrongDoor.Core.Models;

public record NormalizedResident(
    string Source,
    string SourceId,
    string FullName,
    string? DateOfBirth = null,
    string? AddressLine = null,
    string? City = null,
    string? Phone = null,
    string? ProgramStatus = null,
    string? LastContact = null,
    string? BenefitCode = null,
    string? ReviewDue = null
);
