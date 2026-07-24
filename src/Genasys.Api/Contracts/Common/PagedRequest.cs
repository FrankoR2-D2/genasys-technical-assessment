namespace Genasys.Api.Contracts.Common;

public class PagedRequest
{
    private const int MaxPageSize = 100;
    private int _page = 1;
    private int _pageSize = 20;
    private int? _skip;
    private int? _take;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    // Raw offset/limit, for clients that think in terms of rows rather than
    // pages — when supplied, these take precedence over Page/PageSize.
    public int? Skip
    {
        get => _skip;
        set => _skip = value is null ? null : Math.Max(0, value.Value);
    }

    public int? Take
    {
        get => _take;
        set => _take = value is null ? null : Math.Clamp(value.Value, 1, MaxPageSize);
    }

    public string? Search { get; set; }

    // "field:asc" or "field:desc" — validated against a per-resource allow-list in the service, not a raw column name.
    public string? Sort { get; set; }

    public int EffectiveSkip => Skip ?? (Page - 1) * PageSize;
    public int EffectiveTake => Take ?? PageSize;

    // The response envelope always reports page/pageSize, even when the
    // request came in as skip/take, so a client only has to understand one shape.
    public int EffectivePage => EffectiveTake == 0 ? 1 : EffectiveSkip / EffectiveTake + 1;
}
