using UpscaleLab.Application.Images;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ImageFormatPolicyTests
{
    public static TheoryData<string, string, string> MobileFormats => new()
    {
        { "photo.jpg", "image/jpeg", "image/jpeg" },
        { "photo.png", "image/png", "image/png" },
        { "photo.webp", "image/webp", "image/webp" },
        { "photo.gif", "image/gif", "image/gif" },
        { "photo.bmp", "image/x-ms-bmp", "image/bmp" },
        { "photo.tiff", "image/tiff", "image/tiff" },
        { "photo.heic", "image/heic", "image/heic" },
        { "photo.heif", "image/heif", "image/heif" },
        { "photo.avif", "image/avif", "image/avif" },
        { "photo.dng", "image/x-adobe-dng", "image/x-adobe-dng" },
        { "photo.jp2", "image/jp2", "image/jp2" },
        { "photo.wbmp", "image/vnd.wap.wbmp", "image/vnd.wap.wbmp" }
    };

    [Theory]
    [MemberData(nameof(MobileFormats))]
    public void TryResolveContentType_AcceptsMobileImageFormats(
        string fileName,
        string declaredContentType,
        string expectedContentType)
    {
        var accepted = ImageFormatPolicy.TryResolveContentType(
            fileName,
            declaredContentType,
            out var contentType);

        Assert.True(accepted);
        Assert.Equal(expectedContentType, contentType);
    }

    [Fact]
    public void TryResolveContentType_InfersHeicWhenMobileClientUsesGenericContentType()
    {
        var accepted = ImageFormatPolicy.TryResolveContentType(
            "photo.HEIC",
            "application/octet-stream",
            out var contentType);

        Assert.True(accepted);
        Assert.Equal("image/heic", contentType);
    }

    [Fact]
    public void TryResolveContentType_RejectsNonImageFiles()
    {
        var accepted = ImageFormatPolicy.TryResolveContentType(
            "document.pdf",
            "application/pdf",
            out _);

        Assert.False(accepted);
    }
}
