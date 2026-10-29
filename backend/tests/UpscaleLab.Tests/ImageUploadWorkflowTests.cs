using ImageMagick;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ImageUploadWorkflowTests
{
    [Theory]
    [InlineData(MagickFormat.Jpeg, "jpeg", "image/jpeg")]
    [InlineData(MagickFormat.Png, "png", "image/png")]
    [InlineData(MagickFormat.Gif, "gif", "image/gif")]
    [InlineData(MagickFormat.WebP, "webp", "image/webp")]
    [InlineData(MagickFormat.Bmp, "bmp", "image/bmp")]
    [InlineData(MagickFormat.Tiff, "tiff", "image/tiff")]
    [InlineData(MagickFormat.Avif, "avif", "image/avif")]
    public async Task ActualImageFormatsBecomeBrowserReadableAndServerDimensionsOverrideClaims(MagickFormat format, string extension, string type)
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        using var image = new MagickImage(MagickColors.Blue, 40, 20);
        await using var input = new MemoryStream(image.ToByteArray(format));
        var result = await new ProjectService(db, store).CreateAsync(Guid.NewGuid(),
            new(input, "fixture." + extension, type, "Fixture", 999, 999), CancellationToken.None);
        Assert.Equal(40, result.OriginalWidth); Assert.Equal(20, result.OriginalHeight);
        Assert.Contains(result.OriginalContentType, new[] { "image/jpeg", "image/png" });
        using var stored = new MagickImage(Assert.Single(store.Files).Value);
        Assert.Equal(40u, stored.Width); Assert.Equal(20u, stored.Height);
    }

    [Fact]
    public async Task Exactly50MBAcceptsAValidImageAndLargerOrDisguisedFilesCreateNoRowsOrObjects()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var service = new ProjectService(db, store);
        using var image = new MagickImage(MagickColors.Blue, 2, 2);
        await using var input = new MemoryStream();
        await input.WriteAsync(image.ToByteArray(MagickFormat.Png)); input.SetLength(ImageFormatPolicy.MaxUploadBytes); input.Position = 0;
        await service.CreateAsync(Guid.NewGuid(), new(input, "boundary.png", "image/png", "Boundary", 0, 0), CancellationToken.None);
        input.SetLength(ImageFormatPolicy.MaxUploadBytes + 1); input.Position = 0;
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Guid.NewGuid(),
            new(input, "too-large.png", "image/png", "Invalid", 0, 0), CancellationToken.None));
        await using var disguised = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>"));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Guid.NewGuid(),
            new(disguised, "disguised.png", "image/png", "Invalid", 0, 0), CancellationToken.None));
        Assert.Single(await db.LiveLayerProjects.ToListAsync()); Assert.Single(store.Files);
    }
}
