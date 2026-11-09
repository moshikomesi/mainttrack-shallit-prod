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

    /// <summary>
    /// How often a worker extends leases for jobs it still owns.
    /// This stays shorter than <see cref="ProcessingLease"/> so later jobs in a batch
    /// are not reclaimed while an earlier job is still running.
    /// </summary>
    public TimeSpan ClaimRenewalInterval =>
        TimeSpan.FromMilliseconds(Math.Max(200, ProcessingLease.TotalMilliseconds / 3));
}
