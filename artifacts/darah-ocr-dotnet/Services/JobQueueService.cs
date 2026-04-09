using DarahOcr.Data;
using DarahOcr.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace DarahOcr.Services;

public class JobQueueService(
    IServiceScopeFactory scopeFactory,
    JobProgressService progress,
    ILogger<JobQueueService> logger) : BackgroundService
{
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private const int MaxWorkers = 2;
    private const int MaxRetries = 3;

    public void EnqueueJob(int jobId)
    {
        _queue.Enqueue(jobId);
        _signal.Release();
        logger.LogInformation("Job {JobId} enqueued", jobId);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait a moment for DB tables to be created
        await Task.Delay(3000, ct);

        // Resume pending jobs on startup
        await ResumePendingJobsAsync();

        var workers = Enumerable.Range(0, MaxWorkers)
            .Select(_ => WorkerLoop(ct))
            .ToArray();
        await Task.WhenAll(workers);
    }

    private async Task ResumePendingJobsAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.Jobs
            .Where(j => j.Status == "pending" || j.Status == "processing")
            .Select(j => j.Id)
            .ToListAsync();

        foreach (var id in pending)
        {
            _queue.Enqueue(id);
            _signal.Release();
        }
        logger.LogInformation("Resumed {Count} pending jobs", pending.Count);
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _signal.WaitAsync(ct);
            if (!_queue.TryDequeue(out var jobId)) continue;

            try
            {
                await ProcessJobAsync(jobId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error processing job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(int jobId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ocr = scope.ServiceProvider.GetRequiredService<GeminiOcrService>();

        var job = await db.Jobs.FindAsync(jobId);
        if (job == null)
        {
            logger.LogWarning("Job {JobId} not found", jobId);
            return;
        }

        job.Status = "processing";
        job.StartedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            var uploadsDir = Environment.GetEnvironmentVariable("UPLOADS_DIR")
                ?? Path.Combine(AppContext.BaseDirectory, "..", "uploads");
            var filePath = Path.Combine(uploadsDir, job.Filename);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var reportProgress = new Progress<(int page, int total)>(async p =>
            {
                job.ProcessingPage = p.page;
                job.TotalPages = p.total;
                await db.SaveChangesAsync();
                await progress.BroadcastAsync(new JobProgressEvent(jobId, p.page, p.total, "processing"));
            });

            var result = await ocr.ProcessFileAsync(filePath, job.FileType, reportProgress, ct);

            var ocrResult = new OcrResult
            {
                JobId = jobId,
                RawText = result.RawText,
                RefinedText = result.RefinedText,
                ConfidenceScore = result.ConfidenceScore,
                QualityLevel = result.QualityLevel,
                WordCount = result.WordCount,
                PageCount = result.PageCount,
                CreatedAt = DateTime.UtcNow
            };

            // Remove old result if re-processing
            var existing = await db.OcrResults.FirstOrDefaultAsync(r => r.JobId == jobId);
            if (existing != null) db.OcrResults.Remove(existing);

            db.OcrResults.Add(ocrResult);

            job.Status = "ocr_complete";
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            job.ProcessingDurationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
            job.TotalPages = result.PageCount;

            await db.SaveChangesAsync();

            await progress.BroadcastAsync(new JobProgressEvent(jobId, result.PageCount, result.PageCount, "ocr_complete"));

            logger.LogInformation("Job {JobId} completed with {Confidence}% confidence", jobId, result.ConfidenceScore);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OCR failed for job {JobId}", jobId);
            job.Status = job.RetryCount < MaxRetries ? "failed" : "failed";
            job.ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await progress.BroadcastAsync(new JobProgressEvent(jobId, 0, 0, "failed"));
        }
    }
}
