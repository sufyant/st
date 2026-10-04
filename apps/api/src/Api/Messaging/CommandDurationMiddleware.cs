using Microsoft.Extensions.Options;
using Wolverine;

namespace Api.Messaging;

// The duration histogram itself is Wolverine's own (wolverine-execution-time); this adds the configured warning threshold (0022).
// Wolverine's generated code calls it, so it must be public.
public static partial class CommandDurationMiddleware
{
    public static long Before(TimeProvider time) => time.GetTimestamp();

    public static void Finally(
        long startedAt,
        TimeProvider time,
        IOptions<PipelineOptions> options,
        ILogger<Envelope> logger,
        Envelope envelope)
    {
        var elapsed = time.GetElapsedTime(startedAt);
        var threshold = options.Value.SlowCommandThreshold;

        if (elapsed > threshold)
        {
            LogSlowCommand(logger, envelope.MessageType, elapsed.TotalMilliseconds, threshold.TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{MessageType} took {ElapsedMilliseconds} ms, over the {ThresholdMilliseconds} ms threshold")]
    private static partial void LogSlowCommand(ILogger logger, string? messageType, double elapsedMilliseconds, double thresholdMilliseconds);
}
