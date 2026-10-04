namespace Api.Messaging;

// Public because CommandDurationMiddleware, which Wolverine requires to be public, takes it as a parameter.
public sealed class PipelineOptions
{
    public const string Section = "Pipeline";

    public TimeSpan SlowCommandThreshold { get; set; }
}
