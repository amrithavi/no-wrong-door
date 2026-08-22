namespace NoWrongDoor.Core.Interfaces;

using NoWrongDoor.Core.Models;
using ResidentRecord = NoWrongDoor.Core.Models.NormalizedResident;

public interface IResidentSource
{
    Task<SourceResult<ResidentRecord>> GetByIdAsync(string id);
    Task<SourceResult<IReadOnlyList<ResidentRecord>>> SearchAsync(string? name, string? dob);
}
