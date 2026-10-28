using System.Text;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Background;
using UpscaleLab.Api.Configuration;
using UpscaleLab.Api.Hubs;
using UpscaleLab.Api.Middleware;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Devices;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Settings;
using UpscaleLab.Application.Storage;
using UpscaleLab.Application.Upscaling;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Infrastructure.Auth;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Devices;
using UpscaleLab.Infrastructure.Gallery;
using UpscaleLab.Infrastructure.Images;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Settings;
using UpscaleLab.Infrastructure.Storage;
using UpscaleLab.Infrastructure.Upscaling;
using UpscaleLab.Infrastructure.Wallpapers;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is required. Use user-secrets or an environment variable.");
}

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwtOptions.Secret) < 32)
{
    throw new InvalidOperationException("Jwt:Secret must be at least 32 bytes and must come from a secret store.");
}

var s3Options = builder.Configuration.GetSection("AWS").Get<S3StorageOptions>() ?? new S3StorageOptions();
var replicateOptions = builder.Configuration.GetSection("Replicate").Get<ReplicateOptions>() ?? new ReplicateOptions();
var runPodOptions = builder.Configuration
    .GetSection("RunPod")
    .Get<RunPodOptions>() ?? new RunPodOptions();
var seeThroughOptions = builder.Configuration.GetSection("SeeThrough").Get<SeeThroughOptions>() ?? new SeeThroughOptions();
var cognitoOptions = builder.Configuration
    .GetSection(CognitoOptions.SectionName)
    .Get<CognitoOptions>() ?? new CognitoOptions();

if (string.IsNullOrWhiteSpace(cognitoOptions.ClientId))
{
    throw new InvalidOperationException(
        "Cognito:ClientId is required. Use user-secrets or an environment variable.");
}

if (string.IsNullOrWhiteSpace(cognitoOptions.UserPoolId))
    throw new InvalidOperationException("Cognito:UserPoolId is required for expired account cleanup.");

if (string.IsNullOrWhiteSpace(cognitoOptions.Region) || cognitoOptions.ResendCooldownSeconds < 0)
{
    throw new InvalidOperationException("Cognito settings must contain a region and valid positive numeric values.");
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(s3Options);
builder.Services.AddSingleton(replicateOptions);
builder.Services.AddSingleton(runPodOptions);
builder.Services.AddHttpClient<RunPodUpscaleService>(client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton(seeThroughOptions);
builder.Services.AddSingleton(cognitoOptions);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

builder.Services.AddSingleton<IAmazonS3>(_ =>
    new AmazonS3Client(RegionEndpoint.GetBySystemName(s3Options.Region)));
builder.Services.AddScoped<IStorageService, S3StorageService>();
builder.Services.AddScoped<IObjectCopyService, S3StorageService>();
builder.Services.AddHttpClient<IUpscaleService, ReplicateUpscaleService>(client =>
{
    client.BaseAddress = new Uri(replicateOptions.ApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(_ =>
    new AmazonCognitoIdentityProviderClient(
        new AnonymousAWSCredentials(),
        RegionEndpoint.GetBySystemName(cognitoOptions.Region)));

builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailVerificationProvider, CognitoEmailVerificationProvider>();
builder.Services.AddScoped<IPasswordRecoveryProvider, CognitoEmailVerificationProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAccountCleanupService, AccountCleanupService>();
builder.Services.AddScoped<IAccountIdentityRemover, CognitoAccountIdentityRemover>();
builder.Services.AddHostedService<AccountCleanupWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account-sensitive", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<IActiveSessionValidator, ActiveSessionValidator>();
builder.Services.AddScoped<IUnverifiedAccountCleaner, CognitoAccountCleaner>();
builder.Services.AddHostedService<ExpiredAccountWorker>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IDeviceControlService, DeviceControlService>();
builder.Services.AddScoped<IDeviceEvents, DeviceEvents>();
builder.Services.AddSingleton<DeviceChangeFeed>();
builder.Services.AddScoped<IWallpaperService, WallpaperService>();
builder.Services.AddScoped<IUserSettingService, UserSettingService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IGalleryService, GalleryService>();
builder.Services.AddScoped<IGalleryThumbnailService, GalleryThumbnailService>();
builder.Services.AddSingleton<GalleryThumbnailCache>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectProcessingService, ProjectProcessingService>();
builder.Services.AddSingleton<IProcessingJobQueue, ProcessingJobQueue>();
builder.Services.AddSingleton<IImageLayerProcessor, SeeThroughImageLayerProcessor>();
builder.Services.AddSingleton<ExistingLayerProjectProcessor>();
builder.Services.AddSingleton<IProjectImageProcessor, RunPodProjectImageProcessor>();
builder.Services.AddHostedService<LiveLayerProcessingWorker>();

builder.Services.AddClientAuthentication(jwtOptions);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 25 * 1024 * 1024);
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("ClientPolicy", policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
    else
    {
        policy.SetIsOriginAllowed(_ => false);
    }
}));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "LiveLayer API", Version = "v1" });
    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT Bearer token. Example: Bearer eyJ...",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", bearerScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearerScheme] = Array.Empty<string>() });
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("ClientPolicy");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true);

app.Run();

public partial class Program;
