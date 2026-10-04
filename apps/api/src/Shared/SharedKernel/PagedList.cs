namespace SharedKernel;

/// <summary>One page of a list; list endpoints are paginated by default (0034).</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    public PagedList<TOut> Map<TOut>(Func<T, TOut> map) => new([.. Items.Select(map)], Page, PageSize, TotalCount);
}
