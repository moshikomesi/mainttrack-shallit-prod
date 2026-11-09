namespace MaintTrack.Application.Translations;

public sealed class TranslationWorkerOptions
{
    public const string SectionName = "Translation";

    public int PollingIntervalSeconds { get; set; } = 30;

    public int BatchSize { get; set; } = 10;

    public int MaxAttempts { get; set; } = 5;

    public int ProcessingLeaseSeconds { get; set; } = 300;

    public TimeSpan PollingInterval =>
        TimeSpan.FromSeconds(Math.Max(1, PollingIntervalSeconds));

    public int NormalizedBatchSize => Math.Max(1, BatchSize);

    public int NormalizedMaxAttempts => Math.Max(1, MaxAttempts);

    public TimeSpan ProcessingLease =>
        TimeSpan.FromSeconds(Math.Max(1, ProcessingLeaseSeconds));
}
