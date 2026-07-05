namespace MfaLocalDb.Models;

public sealed class ScrapeResult
{
    public required List<ScrapedEntry> Entries { get; init; }

    public required List<string> Failures { get; init; }
}
