namespace MfaLocalDb.Models;

public sealed class ScrapedEntry
{
    public string Kind { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public string ContentText { get; init; } = string.Empty;

    public string ContentHtml { get; init; } = string.Empty;

    public string SyncedAt { get; init; } = string.Empty;
}
