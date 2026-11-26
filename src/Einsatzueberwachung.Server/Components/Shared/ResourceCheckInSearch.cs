namespace Einsatzueberwachung.Server.Components.Shared;

public static class ResourceCheckInSearch
{
    public static bool MatchesQuery(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var term = query.Trim();
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field)
                && field.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
