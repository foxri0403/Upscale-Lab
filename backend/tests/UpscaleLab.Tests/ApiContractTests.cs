using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using UpscaleLab.Api.Controllers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ApiContractTests
{
    [Fact]
    public async Task OpenApi_DescribesTheWebAndAgentContractsWithoutStartingAHost()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddControllers().AddApplicationPart(typeof(AgentController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo { Title = "Contracts", Version = "v1" }));
        await using var app = builder.Build();
        app.MapControllers();
        var api = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.Contains("/api/agent/heartbeat", api.Paths.Keys);
        Assert.Contains("/api/agent/commands/{id}/manifest", api.Paths.Keys);
        Assert.Contains("/api/devices/{id}/commands", api.Paths.Keys);
        Assert.Contains("/api/wallpapers/{id}/import", api.Paths.Keys);
        Assert.Contains("/api/projects/{projectId}/jobs/{jobId}/download-url", api.Paths.Keys);
        Assert.Contains("DeviceState", api.Components.Schemas.Keys);
        Assert.Contains("WallpaperManifest", api.Components.Schemas.Keys);
    }
}
