using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectServiceTests
{
    [Fact]
    public async Task Create_ReusesImageMetadataAndUsesProjectS3Prefix()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeStorageService();
        var service = new ProjectService(dbContext, storage);
        var userId = Guid.NewGuid();
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await service.CreateAsync(
            userId,
            new CreateProjectCommand(stream, "character.png", "image/png", "My Layer", 1280, 720),
            CancellationToken.None);

        var project = await dbContext.LiveLayerProjects.Include(x => x.OriginalImage).SingleAsync();
        Assert.Equal(result.Id, project.Id);
        Assert.Equal(ProjectStatus.Uploaded, project.Status);
        Assert.Equal("character.png", project.OriginalImage.FileName);
        Assert.Contains($"users/{userId:N}/projects/{project.Id:N}/original/", project.OriginalImage.OriginalObjectKey);
        Assert.Equal(3, storage.Objects[project.OriginalImage.OriginalObjectKey].Length);
    }

    [Fact]
    public async Task UpdateLayer_DoesNotExposeAnotherUsersProject()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var project = new LiveLayerProject
        {
            UserId = ownerId,
            Title = "Owner project",
            OriginalImage = new Image
            {
                UserId = ownerId,
                OriginalUrl = "s3://test/original.png",
                OriginalObjectKey = "original.png",
                OriginalWidth = 10,
                OriginalHeight = 10,
                FileName = "original.png",
                ContentType = "image/png"
            }
        };
        var layer = new ImageLayer
        {
            Project = project,
            LayerOrder = 0,
            LayerType = LayerType.Body,
            ImageUrl = "s3://test/layer.png",
            ObjectKey = "layer.png",
            Scale = 1
        };
        dbContext.ImageLayers.Add(layer);
        await dbContext.SaveChangesAsync();
        var service = new ProjectService(dbContext, new FakeStorageService());

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateLayerAsync(
            Guid.NewGuid(),
            project.Id,
            layer.Id,
            new UpdateLayerRequest(1, null, null, null, null, null, null),
            CancellationToken.None));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeStorageService : IStorageService
    {
        public Dictionary<string, byte[]> Objects { get; } = [];

        public async Task<StoredObject> UploadAsync(
            Stream content,
            string objectKey,
            string contentType,
            CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Objects[objectKey] = memory.ToArray();
            return new StoredObject(objectKey, $"s3://test/{objectKey}");
        }

        public Task DownloadAsync(string objectKey, Stream destination, CancellationToken cancellationToken) =>
            destination.WriteAsync(Objects[objectKey], cancellationToken).AsTask();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            Objects.Remove(objectKey);
            return Task.CompletedTask;
        }

        public string CreateDownloadUrl(string objectKey, TimeSpan lifetime) =>
            $"https://example.test/{objectKey}";
    }
}
