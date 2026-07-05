namespace MfaLocalDb.Models;

public class EntryListItem
{
    public long Id { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public string SyncedAt { get; init; } = string.Empty;

    public override string ToString() => Name;
}
