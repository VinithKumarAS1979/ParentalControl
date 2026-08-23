namespace ParentalControl.Common.Models;

/// <summary>A single browser history record captured from a local browser profile.</summary>
public sealed record VisitedSiteEntry(string WindowsUser, string Browser, string Url, string Title, DateTime VisitedAtUtc)
{
    public string ToLogLine() =>
        $"{VisitedAtUtc:yyyy-MM-dd HH:mm:ss} UTC\t{WindowsUser}\t{Browser}\t{Url}\t{Title}";
}

