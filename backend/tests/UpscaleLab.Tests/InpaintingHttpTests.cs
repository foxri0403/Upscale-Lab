using UpscaleLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using ImageMagick;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpscaleLab.Api.Controllers;
using UpscaleLab.Application.Storage;
using UpscaleLab.Infrastructure.Inpainting;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class InpaintingHttpTests
{
    [Fact]
    public async Task AnonymousRequest_Returns401()
    {
        using var server = CreateServer(enabled: true);
        using var client = server.CreateClient();

        using var response = await client.PostAsync(
            "/api/inpainting", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisabledFeature_Returns404()
    {
        using var server = CreateServer(enabled: false);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test-user");

        using var form = new MultipartFormDataContent();

        var image = new ByteArrayContent(
            Encoding.UTF8.GetBytes("test image"));
        image.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        var mask = new ByteArrayContent(
            Encoding.UTF8.GetBytes("test mask"));
        mask.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        form.Add(image, "image", "input.png");
        form.Add(mask, "mask", "mask.png");

        using var response = await client.PostAsync(
            "/api/inpainting", form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvalidPng_Returns400WithoutGpuRequest()
    {
        using var server = CreateServer(enabled: true);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test-user");

        using var form = new MultipartFormDataContent();

        var image = new ByteArrayContent(
            Encoding.UTF8.GetBytes("not a png"));
        image.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        var mask = new ByteArrayContent(
            Encoding.UTF8.GetBytes("not a png"));
        mask.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        form.Add(image, "image", "input.png");
        form.Add(mask, "mask", "mask.png");

        using var response = await client.PostAsync(
            "/api/inpainting", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrFullMask_Returns400WithoutGpuRequest(
        bool whiteMask)
    {
        using var server = CreateServer(enabled: true);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test-user");

        using var image = new MagickImage(MagickColors.White, 8, 8);
        using var mask = new MagickImage(
            whiteMask ? MagickColors.White : MagickColors.Black,
            8, 8);

        using var form = new MultipartFormDataContent();

        var imageContent = new ByteArrayContent(
            image.ToByteArray(MagickFormat.Png));
        imageContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        var maskContent = new ByteArrayContent(
            mask.ToByteArray(MagickFormat.Png));
        maskContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        form.Add(imageContent, "image", "input.png");
        form.Add(maskContent, "mask", "mask.png");

        using var response = await client.PostAsync(
            "/api/inpainting", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static TestServer CreateServer(bool enabled)
    {
        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .ConfigureServices(services =>
            {
                services.AddLogging();

                services
                    .AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        "Test", _ => { });

                services.AddAuthorization();

                services.AddControllers()
                    .AddApplicationPart(
                        typeof(InpaintingController).Assembly);

                services.AddSingleton(
                    new RunPodInpaintingOptions
                    {
                        Enabled = enabled,
                        ApiKey = "test-only-key",
                        EndpointId = "test-only-endpoint"
                    });

                services.AddScoped<IStorageService, NeverCalledStorage>();

                services.AddHttpClient<RunPodInpaintingService>();

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase(
                        Guid.NewGuid().ToString()));

                services.AddScoped<InpaintingQuotaService>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        return new TestServer(builder);
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-User"))
            {
                return Task.FromResult(
                    AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "11111111-1111-4111-8111-111111111111")
                },
                Scheme.Name);

            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity), Scheme.Name);

            return Task.FromResult(
                AuthenticateResult.Success(ticket));
        }
    }

    private sealed class NeverCalledStorage : IStorageService
    {
        public Task<StoredObject> UploadAsync(
            Stream content, string objectKey, string contentType,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Test must not reach S3.");

        public Task DownloadAsync(
            string objectKey, Stream destination,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Test must not reach S3.");

        public Task DeleteAsync(
            string objectKey, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Test must not reach S3.");

        public string CreateDownloadUrl(
            string objectKey, TimeSpan lifetime)
            => throw new InvalidOperationException(
                "Test must not reach S3.");

        public string CreateUploadUrl(
            string objectKey, string contentType, TimeSpan lifetime)
            => throw new InvalidOperationException(
                "Test must not reach S3.");
    }
}
