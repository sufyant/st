namespace Onboarding.EndToEndTests;

// Waits for a state the test can see, such as an email that went out: the onboarding runs in the worker, after the request.
internal static class Waiting
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Patience);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }
}
