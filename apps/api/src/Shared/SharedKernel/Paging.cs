namespace SharedKernel;

/// <summary>
/// The page a client asks of a list (OWASP API4). Pages count from 1; a page holds 50 items unless the client asks for 1 to 100.
/// </summary>
public sealed record PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    /// <summary>The items before this page; a page beyond what an offset can count is simply empty.</summary>
    public int Skip => (int)Math.Min((long)(Page - 1) * PageSize, int.MaxValue);

    /// <summary>The page the client asked for, from what it sent; a value it left out takes the default.</summary>
    public static Result<PageRequest> Create(int? page, int? pageSize)
    {
        if (page is < 1)
        {
            return Error.Validation("paging.page_invalid", "The page starts at 1.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return Error.Validation("paging.page_size_invalid", $"The page size is 1 to {MaxPageSize}.");
        }

        return new PageRequest(page ?? 1, pageSize ?? DefaultPageSize);
    }
}

/// <summary>One page of a list, and how many items the whole list holds.</summary>
public sealed record ListPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public ListPage(IReadOnlyList<T> items, PageRequest request, int total)
        : this(items, request.Page, request.PageSize, total)
    {
    }

    public ListPage<TOut> Map<TOut>(Func<T, TOut> map) => new([.. Items.Select(map)], Page, PageSize, Total);
}
