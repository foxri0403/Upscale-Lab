using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Controllers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class InpaintingControllerTests
{
    private static readonly Type ControllerType =
        typeof(InpaintingController);

    [Fact]
    public void Controller_RequiresAuthentication()
    {
        Assert.NotNull(
            ControllerType.GetCustomAttribute<AuthorizeAttribute>());

        Assert.Null(
            ControllerType.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Controller_UsesExpectedRoute()
    {
        var route =
            ControllerType.GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.Equal("api/inpainting", route.Template);
    }

    [Fact]
    public void Inpaint_UsesPostAndMultipart()
    {
        var method = ControllerType.GetMethod("Inpaint");

        Assert.NotNull(method);

        var post = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(post);

        var consumes =
            method.GetCustomAttribute<ConsumesAttribute>();

        Assert.NotNull(consumes);
        Assert.Contains(
            "multipart/form-data",
            consumes.ContentTypes);
    }

    [Fact]
    public void Inpaint_HasRequestSizeLimit()
    {
        var method = ControllerType.GetMethod("Inpaint");
        Assert.NotNull(method);

        var limit =
            method.GetCustomAttribute<RequestSizeLimitAttribute>();

        Assert.NotNull(limit);
    }

    [Fact]
    public void Inpaint_RequiresImageAndMaskParameters()
    {
        var method = ControllerType.GetMethod("Inpaint");
        Assert.NotNull(method);

        var parameters = method.GetParameters();

        var image = Assert.Single(
            parameters, p => p.Name == "image");

        var mask = Assert.Single(
            parameters, p => p.Name == "mask");

        Assert.Equal(typeof(IFormFile), image.ParameterType);
        Assert.Equal(typeof(IFormFile), mask.ParameterType);
    }

    [Fact]
    public void Inpaint_DoesNotAllowAnonymousOverride()
    {
        var method = ControllerType.GetMethod("Inpaint");

        Assert.NotNull(method);

        Assert.Null(
            method.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
