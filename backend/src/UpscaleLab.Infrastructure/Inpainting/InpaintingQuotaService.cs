using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Inpainting;

public sealed class InpaintingQuotaService(
    ApplicationDbContext db)
{
    private const int DailyLimit = 5;

    public static (DateTime StartUtc, DateTime EndUtc)
        GetKoreanDayBounds(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException(
                "A UTC timestamp is required.", nameof(utcNow));

        var koreaNow = utcNow.AddHours(9);
        var startUtc = DateTime.SpecifyKind(
            koreaNow.Date.AddHours(-9), DateTimeKind.Utc);

        return (startUtc, startUtc.AddDays(1));
    }

    public async Task<InpaintingJob?> ReserveAsync(
        Guid userId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var (startUtc, endUtc) = GetKoreanDayBounds(now);

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        // Serialize reservations for the same user in PostgreSQL.
        await using var command =
            db.Database.GetDbConnection().CreateCommand();

        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT 1 FROM users WHERE \"Id\" = @userId FOR UPDATE";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "userId";
        parameter.Value = userId;
        command.Parameters.Add(parameter);

        var lockedUser = await command.ExecuteScalarAsync(ct);

        if (lockedUser is null)
            throw new InvalidOperationException(
                "User does not exist.");

        var used = await db.InpaintingJobs.CountAsync(
            job => job.UserId == userId &&
                   job.CreatedAt >= startUtc &&
                   job.CreatedAt < endUtc,
            ct);

        if (used >= DailyLimit)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        var job = new InpaintingJob
        {
            UserId = userId,
            Status = ProcessingJobStatus.Processing,
            StartedAt = now
        };

        db.InpaintingJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return job;
    }

    public async Task SaveRunPodJobIdAsync(
        Guid jobId,
        string runPodJobId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runPodJobId) ||
            runPodJobId.Length > 128)
        {
            throw new ArgumentException(
                "Invalid RunPod job ID.", nameof(runPodJobId));
        }

        var job = await db.InpaintingJobs
            .SingleAsync(x => x.Id == jobId, ct);

        job.RunPodJobId = runPodJobId;

        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(
        Guid jobId,
        string resultObjectKey,
        CancellationToken ct)
    {
        var job = await db.InpaintingJobs
            .SingleAsync(x => x.Id == jobId, ct);

        job.Status = ProcessingJobStatus.Completed;
        job.ResultObjectKey = resultObjectKey;
        job.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(
        Guid jobId,
        string errorMessage,
        CancellationToken ct)
    {
        var job = await db.InpaintingJobs
            .SingleAsync(x => x.Id == jobId, ct);

        job.Status = ProcessingJobStatus.Failed;
        job.ErrorMessage = errorMessage.Length > 2000
            ? errorMessage[..2000]
            : errorMessage;
        job.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
