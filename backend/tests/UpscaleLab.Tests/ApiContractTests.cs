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
        Assert.Contains("/api/inpainting", api.Paths.Keys);
        Assert.Contains(
            OperationType.Post,
            api.Paths["/api/inpainting"].Operations.Keys);
        Assert.Contains("/api/agent/heartbeat", api.Paths.Keys);
        Assert.Contains("/api/agent/commands/{id}/manifest", api.Paths.Keys);
        Assert.Contains("/api/devices/{id}/commands", api.Paths.Keys);
        Assert.Contains(OperationType.Post, api.Paths["/api/devices/{id}/commands"].Operations.Keys);
        Assert.DoesNotContain(OperationType.Get, api.Paths["/api/devices/{id}/commands"].Operations.Keys);
        Assert.Contains("/api/wallpapers/{id}/import", api.Paths.Keys);
        Assert.Contains(OperationType.Post, api.Paths["/api/gallery/{id}/import"].Operations.Keys);
        Assert.Contains("/api/projects/{projectId}/jobs/{jobId}/download-url", api.Paths.Keys);
        Assert.Contains("multipart/form-data", api.Paths["/api/projects/{projectId}/subject-mask"].Operations[OperationType.Post].RequestBody.Content.Keys);
        Assert.Contains("DeviceState", api.Components.Schemas.Keys);
        foreach (var field in new[] { "manufacturer", "model", "architecture" })
        {
            Assert.DoesNotContain(field, api.Components.Schemas["DeviceState"].Properties.Keys);
            Assert.DoesNotContain(field, api.Components.Schemas["AgentHeartbeat"].Properties.Keys);
        }
        Assert.Contains("WallpaperManifest", api.Components.Schemas.Keys);
        Assert.Contains(OperationType.Patch, api.Paths["/api/projects/{projectId}"].Operations.Keys);
        Assert.Contains(OperationType.Patch, api.Paths["/api/wallpapers/{id}"].Operations.Keys);
    }
}
