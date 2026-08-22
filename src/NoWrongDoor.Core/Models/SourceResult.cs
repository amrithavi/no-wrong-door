namespace NoWrongDoor.Core.Models;

public record SourceResult<T>(SourceStatus Status, T? Data = default, string? Note = null);
