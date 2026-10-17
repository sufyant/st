using System.Diagnostics.CodeAnalysis;

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
            return Error.Validation(PagingErrorCodes.PageInvalid, "The page starts at 1.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return Error.Validation(PagingErrorCodes.PageSizeInvalid, $"The page size is 1 to {MaxPageSize}.");
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

/// <summary>
/// What a client asks of a list (K5): the page, one sort field and its direction, and a search. The sort fields are the members of
/// <typeparamref name="TSort"/>, and the first member is the list's default. The search is a case-insensitive "contains" match on the
/// list's search fields; null is no search.
/// </summary>
public sealed record ListRequest<TSort>(PageRequest Paging, TSort Sort, bool Descending, string? Search)
    where TSort : struct, Enum;

public static class ListRequest
{
    public const int SearchMaxLength = 100;

    /// <summary>
    /// The list the client asked for, from what it sent. The sort is one field (JSON:API 1.1, "Sorting"), descending with a leading
    /// <c>-</c>; left out or empty, it is the first field, ascending. The search is trimmed; an empty one is no search.
    /// </summary>
    public static Result<ListRequest<TSort>> Create<TSort>(int? page, int? pageSize, ListSort<TSort>? sort, string? q)
        where TSort : struct, Enum
    {
        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var text = sort?.Text ?? string.Empty;
        var descending = text.StartsWith('-');
        var field = descending ? text[1..] : text;
        var sortField = text.Length == 0 ? JsonEnum<TSort>.First : JsonEnum<TSort>.Find(field);
        if (sortField is not { } chosen)
        {
            return Error.Validation(PagingErrorCodes.SortInvalid, $"The sort is one of: {string.Join(", ", ListSort.ValuesOf<TSort>())}.");
        }

        var search = q?.Trim();
        if (search?.Length > SearchMaxLength)
        {
            return Error.Validation(PagingErrorCodes.QInvalid, $"The search is at most {SearchMaxLength} characters, not counting the spaces around it.");
        }

        return new ListRequest<TSort>(paging.Value, chosen, descending, string.IsNullOrEmpty(search) ? null : search);
    }
}

/// <summary>
/// The <c>sort</c> query parameter of a list, as the client sent it. Binding keeps any text, so that <see cref="ListRequest"/> checks
/// it and an unknown field answers with a validation code. The OpenAPI document lists its values (<see cref="ListSort.ValuesOf"/>).
/// </summary>
public readonly record struct ListSort<TField>(string Text)
    where TField : struct, Enum
{
    [SuppressMessage("Design", "CA1000", Justification = "Minimal API binding calls a public static TryParse on the parameter's type.")]
    public static bool TryParse(string? text, out ListSort<TField> sort)
    {
        sort = new(text ?? string.Empty);
        return true;
    }
}

public static class ListSort
{
    /// <summary>Each sort field by its JSON name, ascending, then descending with a leading <c>-</c>.</summary>
    public static IReadOnlyList<string> ValuesOf<TField>()
        where TField : struct, Enum => [.. JsonEnum<TField>.Names.SelectMany(name => new[] { name, $"-{name}" })];
}

/// <summary>
/// A filter query parameter of a list, as the client sent it. Its values are the JSON names of <typeparamref name="TValue"/>, the
/// values the same field has in the response. Binding keeps any text, so that <see cref="ListFilter.Read"/> checks it and an unknown
/// value answers with a validation code.
/// </summary>
public readonly record struct ListFilter<TValue>(string Text)
    where TValue : struct, Enum
{
    [SuppressMessage("Design", "CA1000", Justification = "Minimal API binding calls a public static TryParse on the parameter's type.")]
    public static bool TryParse(string? text, out ListFilter<TValue> filter)
    {
        filter = new(text ?? string.Empty);
        return true;
    }
}

public static class ListFilter
{
    /// <summary>The JSON name of each value.</summary>
    public static IReadOnlyList<string> ValuesOf<TValue>()
        where TValue : struct, Enum => JsonEnum<TValue>.Names;

    /// <summary>The value the client chose; null when the filter is left out or empty, as an empty sort or search is.</summary>
    public static Result<TValue?> Read<TValue>(ListFilter<TValue>? filter, string name)
        where TValue : struct, Enum
    {
        if (filter is not { Text.Length: > 0 } sent)
        {
            return (TValue?)null;
        }

        return JsonEnum<TValue>.Find(sent.Text) is { } value
            ? value
            : Error.Validation(PagingErrorCodes.FilterInvalid, $"The filter {name} is one of: {string.Join(", ", ValuesOf<TValue>())}.");
    }
}
