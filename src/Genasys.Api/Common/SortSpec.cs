namespace Genasys.Api.Common;

// Parses "field:asc|desc" from a query string. Each service maps Field
// against its own allow-list of sortable columns — never a raw SQL identifier.
public readonly record struct SortSpec(string Field, bool Descending)
{
    public static SortSpec Parse(string? sort, string defaultField)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return new SortSpec(defaultField, false);
        }

        var parts = sort.Split(':', 2, StringSplitOptions.TrimEntries);
        var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return new SortSpec(parts[0], descending);
    }
}
