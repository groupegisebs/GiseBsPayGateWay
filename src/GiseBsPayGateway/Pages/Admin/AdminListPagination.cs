namespace GiseBsPayGateway.Pages.Admin;

public static class AdminListPagination
{
    public const int PageSize = 15;

    public static (int Page, string? Search) Parse(int page, string? search)
    {
        page = page < 1 ? 1 : page;
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return (page, search);
    }

    public static AdminPaginationInfo Create(int page, string? search, int totalCount, string? extraQuery = null)
    {
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        page = Math.Min(Math.Max(page, 1), totalPages);
        return new AdminPaginationInfo(page, search, totalCount, totalPages, PageSize, extraQuery);
    }
}

public sealed class AdminPaginationInfo
{
    public AdminPaginationInfo(int page, string? search, int totalCount, int totalPages, int pageSize, string? extraQuery = null)
    {
        Page = page;
        Search = search;
        TotalCount = totalCount;
        TotalPages = totalPages;
        PageSize = pageSize;
        ExtraQuery = string.IsNullOrWhiteSpace(extraQuery) ? null : extraQuery.Trim().TrimStart('&');
    }

    public int Page { get; }
    public string? Search { get; }
    public int TotalCount { get; }
    public int TotalPages { get; }
    public int PageSize { get; }
    public string? ExtraQuery { get; }

    public int Skip => (Page - 1) * PageSize;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
    public int StartItem => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int EndItem => Math.Min(Page * PageSize, TotalCount);

    public string PageUrl(int page)
    {
        var parts = new List<string> { $"page={page}" };
        if (!string.IsNullOrEmpty(Search))
            parts.Add($"search={Uri.EscapeDataString(Search)}");
        if (!string.IsNullOrEmpty(ExtraQuery))
            parts.Add(ExtraQuery);
        return "?" + string.Join("&", parts);
    }
}
