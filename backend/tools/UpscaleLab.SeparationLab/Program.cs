using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Background;
using UpscaleLab.Api.Configuration;
using UpscaleLab.Api.Controllers;
using UpscaleLab.Api.Middleware;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Auth;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Storage;
using UpscaleLab.Infrastructure.Upscaling;
using UpscaleLab.SeparationLab;

// A development harness. The production API entry point and PostgreSQL are never started here.
var builder = WebApplication.CreateBuilder(args.Where(x => x != "--cleanup").ToArray());
builder.Logging.ClearProviders(); builder.Logging.AddConsole(); builder.Logging.SetMinimumLevel(LogLevel.Warning);
var storage = builder.Configuration.GetSection("AWS").Get<S3StorageOptions>() ?? new();
var runpod = builder.Configuration.GetSection("RunPod").Get<RunPodOptions>() ?? new();
if (string.IsNullOrWhiteSpace(storage.S3BucketName) || string.IsNullOrWhiteSpace(runpod.LayersEndpointId) ||
    string.IsNullOrWhiteSpace(runpod.LayersApiKey) && string.IsNullOrWhiteSpace(runpod.ApiKey))
    throw new InvalidOperationException("S3 and cloud layer processing must be configured for this lab.");
var journalPath = Environment.GetEnvironmentVariable("UPSCALE_LAB_JOURNAL")
    ?? throw new InvalidOperationException("An isolated lab journal path is required.");
var journal = LabJournal.Open(journalPath);
using var s3 = new AmazonS3Client(RegionEndpoint.GetBySystemName(storage.Region));
if (args.Contains("--cleanup")) { await journal.CleanupAsync(s3, storage.S3BucketName, CancellationToken.None); return; }
var jwt = new JwtOptions { Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(48)),
    Issuer = "UpscaleLab.SeparationLab", Audience = "UpscaleLab.SeparationLab", ExpirationMinutes = 720 };
builder.Services.AddSingleton(jwt); builder.Services.AddSingleton(storage); builder.Services.AddSingleton(runpod);
builder.Services.AddSingleton(journal); builder.Services.AddSingleton<IAmazonS3>(s3); builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("separation-lab-" + journal.UserId));
builder.Services.AddScoped<IStorageService, LabStorage>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectProcessingService, ProjectProcessingService>();
builder.Services.AddSingleton<IProcessingJobQueue, LabQueue>();
builder.Services.AddHttpClient<RunPodImageLayerProcessor>(client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<IImageLayerProcessor>(services => services.GetRequiredService<RunPodImageLayerProcessor>());
builder.Services.AddScoped<IProjectImageProcessor, ExistingLayerProjectProcessor>();
builder.Services.AddScoped<IActiveSessionValidator, ActiveSessionValidator>();
builder.Services.AddClientAuthentication(jwt, includeDeviceAgents: false);
builder.Services.AddHostedService<LiveLayerProcessingWorker>();
builder.Services.AddControllers().AddApplicationPart(typeof(ProjectsController).Assembly)
    .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new LabControllersOnly()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 52 * 1024 * 1024);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 52 * 1024 * 1024);
var app = builder.Build();
var user = new User { Id = journal.UserId, Email = $"{journal.UserId:N}@separation-lab.invalid",
    Username = "separation_lab", PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), IsEmailVerified = true, EmailVerifiedAt = DateTime.UtcNow };
var session = new AuthSession { User = user, ExpiresAt = DateTime.UtcNow.AddHours(12),
    PasswordFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash))),
    TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.Add(session); await db.SaveChangesAsync();
}
var tokenService = new JwtTokenService(jwt);
app.Use(async (context, next) => {
    var origin = context.Request.Headers.Origin.ToString();
    if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
        (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsLoopback)) ||
        (context.Request.Path != "/health" && context.Request.Headers["X-Upscale-Separation-Lab"] != "1")) {
        context.Response.StatusCode = 403; return;
    }
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseMiddleware<ExceptionHandlingMiddleware>(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health", async (ApplicationDbContext db, CancellationToken ct) => Results.Ok(new {
    ready = true, engine = "BiRefNet + LaMa", isolated = true,
    activeJobs = await db.ProcessingJobs.CountAsync(x => x.Status == ProcessingJobStatus.Queued || x.Status == ProcessingJobStatus.Processing, ct)
}));
app.MapGet("/api/lab/session", () => {
    var (token, expiresAt) = tokenService.Create(user, session.Id);
    return Results.Ok(new { accessToken = token, expiresAt, userId = user.Id });
});
app.MapPost("/api/lab/cleanup", async (ApplicationDbContext db, IProjectService projects, CancellationToken ct) => {
    if (await db.ProcessingJobs.AnyAsync(x => x.Status == ProcessingJobStatus.Queued || x.Status == ProcessingJobStatus.Processing, ct))
        return Results.Conflict(new { detail = "처리가 끝난 뒤 테스트 파일을 정리해 주세요." });
    foreach (var id in await db.LiveLayerProjects.Where(x => x.UserId == journal.UserId).Select(x => x.Id).ToListAsync(ct))
        await projects.DeleteAsync(journal.UserId, id, ct);
    await journal.CleanupAsync(s3, storage.S3BucketName, ct); return Results.Ok(new { cleaned = true });
}).RequireAuthorization("AccountSession");
app.MapControllers(); await app.RunAsync();

internal sealed class LabControllersOnly : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var controller in feature.Controllers.Where(x => x.AsType() != typeof(ProjectsController)).ToList()) feature.Controllers.Remove(controller);
    }
}
