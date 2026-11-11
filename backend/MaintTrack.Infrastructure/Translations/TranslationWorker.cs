using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Translations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaintTrack.Infrastructure.Translations;

/// <summary>
/// Polls translation jobs outside the request pipeline.
/// Without a registered <see cref="ITranslationProvider"/> it does not read or write translation rows.
/// </summary>
public sealed class TranslationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranslationWorkerOptions _options;
    private readonly ILogger<TranslationWorker> _logger;

    public TranslationWorker(
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<TranslationWorkerOptions> options,
        ILogger<TranslationWorker> logger)
        : this(scopeFactory, options.Value, logger)
    {
    }

    internal TranslationWorker(
        IServiceScopeFactory scopeFactory,
        TranslationWorkerOptions options,
        ILogger<TranslationWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Translation worker iteration failed.");
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        List<ClaimedJob> claimed;
        await using (var claimScope = _scopeFactory.CreateAsyncScope())
        {
            if (claimScope.ServiceProvider.GetService<ITranslationProvider>() is null)
            {
                _logger.LogDebug("Translation provider is not configured.");
                return 0;
            }

            var repository = claimScope.ServiceProvider.GetRequiredService<ITranslationRepository>();
            var jobs = await repository.ClaimPendingAsync(
                _options.NormalizedBatchSize,
                _options.ProcessingLease,
                cancellationToken);

            claimed = jobs
                .Select(job => new ClaimedJob(
                    job.Id,
                    job.ClaimToken ?? throw new InvalidOperationException("Claimed translation job has no claim token."),
                    job.TenantId,
                    job.SourceText,
                    job.SourceHash,
                    job.TargetLanguage))
                .ToList();
        }

        if (claimed.Count == 0)
            return 0;

        var claims = claimed
            .Select(job => new TranslationClaim(job.Id, job.ClaimToken))
            .ToArray();
        using var renewalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewal = RenewClaimsWhileProcessingAsync(claims, renewalCancellation.Token);
        try
        {
            foreach (var job in claimed)
            {
                try
                {
                    await TranslateClaimedJobAsync(job, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Translation job {JobId} failed.", job.Id);
                    await RecordFailureAsync(job, exception, cancellationToken);
                }
            }
        }
        finally
        {
            renewalCancellation.Cancel();
            await renewal;
        }

        return claimed.Count;
    }

    internal Task RunUntilCancelledAsync(CancellationToken stoppingToken) =>
        ExecuteAsync(stoppingToken);

    private async Task TranslateClaimedJobAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.TenantId = job.TenantId;

        var provider = scope.ServiceProvider.GetRequiredService<ITranslationProvider>();
        var repository = scope.ServiceProvider.GetRequiredService<ITranslationRepository>();
        var translated = await provider.TranslateAsync(
            job.SourceText,
            sourceLanguage: null,
            job.TargetLanguage,
            cancellationToken);

        if (translated is null)
            throw new InvalidOperationException("Translation provider returned no text.");

        var result = await repository.CompleteAsync(
            job.Id,
            job.ClaimToken,
            job.SourceHash,
            translated,
            cancellationToken);

        if (result != TranslationCompletionResult.Completed)
        {
            _logger.LogInformation(
                "Translation job {JobId} was not stored ({Result}).",
                job.Id,
                result);
        }
    }

    private async Task RecordFailureAsync(
        ClaimedJob job,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.TenantId = job.TenantId;
            var repository = scope.ServiceProvider.GetRequiredService<ITranslationRepository>();
            var recorded = await repository.FailAsync(
                job.Id,
                job.ClaimToken,
                exception.Message,
                _options.NormalizedMaxAttempts,
                cancellationToken);
            if (!recorded)
            {
                _logger.LogInformation(
                    "Translation job {JobId} failure was not recorded because the claim is no longer current.",
                    job.Id);
            }
        }
        catch (Exception recordException)
        {
            _logger.LogError(
                recordException,
                "Failed to record the outcome of translation job {JobId}.",
                job.Id);
        }
    }

    private async Task RenewClaimsWhileProcessingAsync(
        IReadOnlyList<TranslationClaim> claims,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.ClaimRenewalInterval, cancellationToken);
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<ITranslationRepository>();
                await repository.RenewClaimsAsync(claims, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Translation claim renewal failed.");
            }
        }
    }

    private sealed record ClaimedJob(
        Guid Id,
        Guid ClaimToken,
        Guid TenantId,
        string SourceText,
        string SourceHash,
        string TargetLanguage);
}
