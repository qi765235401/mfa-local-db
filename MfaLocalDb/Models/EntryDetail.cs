namespace MfaLocalDb.Models;

public sealed class EntryDetail : EntryListItem
{
    public string ContentText { get; init; } = string.Empty;

    public string ContentHtml { get; init; } = string.Empty;
}
