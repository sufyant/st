namespace Api.Application;

/// Pipeline behaviors that short-circuit (validation, permission) need to
/// construct a failure of whatever TResponse the current request declares
/// (always Result or Result{T} by this codebase's convention) without
/// knowing T at compile time -- this reflects it.
internal static class ResultReflectionHelper
{
    public static TResponse CreateFailure<TResponse>(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = responseType.GetGenericArguments()[0];
            var failureMethod = typeof(Result)
                .GetMethods()
                .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(valueType);
            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"{typeof(TResponse)} is not a supported response type — expected Result or Result<T>.");
    }
}
