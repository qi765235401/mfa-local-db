namespace MfaLocalDb.Models;

public sealed class ScrapeTarget
{
    public string Kind { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}
