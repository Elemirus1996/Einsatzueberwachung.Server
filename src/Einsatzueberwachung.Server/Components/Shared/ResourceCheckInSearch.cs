namespace Einsatzueberwachung.Server.Components.Shared;

public static class ResourceCheckInSearch
{
    public const int MinimumQueryLength = 3;

    public static bool HasMinimumQueryLength(string? query) =>
        !string.IsNullOrWhiteSpace(query)
        && query.Trim().Length >= MinimumQueryLength;
}
