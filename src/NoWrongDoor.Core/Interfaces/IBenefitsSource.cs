namespace NoWrongDoor.Core.Interfaces;

using NoWrongDoor.Core.Models;
using ResidentRecord = NoWrongDoor.Core.Models.NormalizedResident;

public interface IBenefitsSource
{
    Task<SourceResult<ResidentRecord>> GetByRefAsync(string reference);
    Task<SourceResult<IReadOnlyList<ResidentRecord>>> SearchAsync(string? name, string? dob);
}
