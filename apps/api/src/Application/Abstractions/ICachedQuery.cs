namespace Application.Abstractions;

public interface ICachedQuery
{
    string CacheKey { get; }

    TimeSpan Duration { get; }
}
