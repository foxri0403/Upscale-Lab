using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;

namespace UpscaleLab.Infrastructure.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<OptimizedImage> OptimizedImages => Set<OptimizedImage>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<GalleryPost> GalleryPosts => Set<GalleryPost>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<GalleryLike> GalleryLikes => Set<GalleryLike>();
    public DbSet<LiveLayerProject> LiveLayerProjects => Set<LiveLayerProject>();
    public DbSet<ImageLayer> ImageLayers => Set<ImageLayer>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<DeviceDisplay> DeviceDisplays => Set<DeviceDisplay>();
    public DbSet<WallpaperAsset> WallpaperAssets => Set<WallpaperAsset>();
    public DbSet<WallpaperAssignment> WallpaperAssignments => Set<WallpaperAssignment>();
    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        var session = modelBuilder.Entity<AuthSession>();
        session.ToTable("auth_sessions");
        session.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        session.Property(x => x.PasswordFingerprint).HasMaxLength(64).IsRequired();
        session.Property(x => x.RevokedAt).IsConcurrencyToken();
        session.HasIndex(x => x.TokenHash).IsUnique();
        session.HasIndex(x => x.ExpiresAt);
        session.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        ConfigureDevice(modelBuilder);
        ConfigureImage(modelBuilder);
        ConfigureOptimizedImage(modelBuilder);
        ConfigureUserSetting(modelBuilder);
        ConfigureGallery(modelBuilder);
        ConfigureLiveLayer(modelBuilder);
        ConfigureWallpaperControl(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyUtcTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyUtcTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<User>();
        entity.ToTable("users");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
        entity.Property(x => x.Username).HasMaxLength(50).IsRequired();
        entity.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => x.Email).IsUnique();
        entity.HasIndex(x => x.Username).IsUnique();
    }

    private static void ConfigureDevice(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Device>();
        entity.ToTable("devices");
        entity.Property(x => x.Platform).HasConversion<string>().HasMaxLength(20);
        entity.Property(x => x.Orientation).HasConversion<string>().HasMaxLength(20);
        entity.Property(x => x.DeviceName).HasMaxLength(120).IsRequired();
        entity.Property(x => x.AspectRatio).HasMaxLength(32).IsRequired();
        entity.HasIndex(x => x.UserId);
        entity.Property(x => x.AgentTokenHash).HasMaxLength(64);
        entity.HasIndex(x => x.AgentTokenHash).IsUnique();
        entity.Property(x => x.OsVersion).HasMaxLength(120);
        entity.Property(x => x.AgentVersion).HasMaxLength(40);
        entity.Property(x => x.Manufacturer).HasMaxLength(120);
        entity.Property(x => x.Model).HasMaxLength(120);
        entity.Property(x => x.Architecture).HasMaxLength(32);
        entity.Property(x => x.ControlVersion).IsConcurrencyToken();
        entity.HasOne(x => x.User).WithMany(x => x.Devices).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureImage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Image>();
        entity.ToTable("images");
        entity.Property(x => x.OriginalUrl).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.OriginalObjectKey).HasMaxLength(1024).IsRequired();
        entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        entity.HasOne(x => x.User).WithMany(x => x.Images).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureOptimizedImage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OptimizedImage>();
        entity.ToTable("optimized_images");
        entity.Property(x => x.Orientation).HasConversion<string>().HasMaxLength(20);
        entity.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.ObjectKey).HasMaxLength(1024).IsRequired();
        entity.HasIndex(x => new { x.ImageId, x.DeviceId, x.Orientation }).IsUnique();
        entity.HasOne(x => x.Image).WithMany(x => x.OptimizedImages).HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Device).WithMany(x => x.OptimizedImages).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureUserSetting(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserSetting>();
        entity.ToTable("user_settings");
        entity.HasIndex(x => x.UserId).IsUnique();
        entity.Property(x => x.SensorSensitivity).HasDefaultValue(1d);
        entity.HasOne(x => x.User).WithOne(x => x.Setting).HasForeignKey<UserSetting>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureGallery(ModelBuilder modelBuilder)
    {
        var post = modelBuilder.Entity<GalleryPost>();
        post.ToTable("gallery_posts");
        post.Property(x => x.Title).HasMaxLength(160).IsRequired();
        post.Property(x => x.Tag).HasMaxLength(50).HasDefaultValue("장르 설정되지 않음").IsRequired();
        post.Property(x => x.Tags).HasColumnType("text[]").IsRequired();
        post.HasIndex(x => x.Tags).HasMethod("gin");
        post.Property(x => x.Description).HasMaxLength(2000);
        post.Property(x => x.IsPublic).HasDefaultValue(true);
        post.HasIndex(x => x.CreatedAt);
        post.HasOne(x => x.User).WithMany(x => x.GalleryPosts).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        post.HasOne(x => x.Image).WithMany(x => x.GalleryPosts).HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Cascade);
        post.HasIndex(x => new { x.IsPublic, x.CreatedAt });
        post.HasIndex(x => new { x.Tag, x.CreatedAt });
        post.HasOne(x => x.Project).WithMany(x => x.GalleryPosts).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);

        var comment = modelBuilder.Entity<Comment>();
        comment.ToTable("comments");
        comment.Property(x => x.Content).HasMaxLength(1000).IsRequired();
        comment.HasIndex(x => new { x.GalleryPostId, x.CreatedAt });
        comment.HasOne(x => x.GalleryPost).WithMany(x => x.Comments).HasForeignKey(x => x.GalleryPostId).OnDelete(DeleteBehavior.Cascade);
        comment.HasOne(x => x.User).WithMany(x => x.Comments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var like = modelBuilder.Entity<GalleryLike>();
        like.ToTable("gallery_likes");
        like.HasIndex(x => new { x.GalleryPostId, x.UserId }).IsUnique();
        like.HasOne(x => x.GalleryPost).WithMany(x => x.Likes).HasForeignKey(x => x.GalleryPostId).OnDelete(DeleteBehavior.Cascade);
        like.HasOne(x => x.User).WithMany(x => x.Likes).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLiveLayer(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<LiveLayerProject>();
        project.ToTable("live_layer_projects");
        project.Property(x => x.Title).HasMaxLength(160).IsRequired();
        project.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        project.Property(x => x.FailureReason).HasMaxLength(2000);
        project.HasIndex(x => new { x.UserId, x.CreatedAt });
        project.HasOne(x => x.User).WithMany(x => x.Projects).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        project.HasOne(x => x.OriginalImage).WithMany(x => x.LiveLayerProjects).HasForeignKey(x => x.OriginalImageId).OnDelete(DeleteBehavior.Restrict);

        var layer = modelBuilder.Entity<ImageLayer>();
        layer.ToTable("image_layers");
        layer.Property(x => x.LayerType).HasConversion<string>().HasMaxLength(30);
        layer.Property(x => x.ImageUrl).HasMaxLength(2048).IsRequired();
        layer.Property(x => x.ObjectKey).HasMaxLength(1024).IsRequired();
        layer.HasIndex(x => new { x.ProjectId, x.LayerOrder }).IsUnique();
        layer.HasOne(x => x.Project).WithMany(x => x.Layers).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

        var job = modelBuilder.Entity<ProcessingJob>();
        job.ToTable("processing_jobs");
        job.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        job.Property(x => x.ErrorMessage).HasMaxLength(2000);
        job.Property(x => x.Operation).HasConversion<string>().HasMaxLength(30).HasDefaultValue(UpscaleLab.Domain.Enums.ProcessingOperation.Layers);
        job.Property(x => x.UpscaleFactor).HasDefaultValue(2);
        job.Property(x => x.ResultObjectKey).HasMaxLength(1024);
        job.HasIndex(x => new { x.ProjectId, x.CreatedAt });
        job.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"Status\" IN ('Queued', 'Processing')");
        job.HasOne(x => x.Project).WithMany(x => x.ProcessingJobs).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureWallpaperControl(ModelBuilder builder)
    {
        var display = builder.Entity<DeviceDisplay>();
        display.ToTable("device_displays");
        display.Property(x => x.NativeId).HasMaxLength(200).IsRequired();
        display.Property(x => x.Name).HasMaxLength(120).IsRequired();
        display.Property(x => x.CanApplyWallpaper).HasDefaultValue(true);
        display.HasIndex(x => new { x.DeviceId, x.NativeId }).IsUnique();
        display.HasOne(x => x.Device).WithMany(x => x.Displays).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);

        var asset = builder.Entity<WallpaperAsset>();
        asset.ToTable("wallpaper_assets");
        asset.Property(x => x.Title).HasMaxLength(160).IsRequired();
        asset.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        asset.Property(x => x.ManifestJson).HasColumnType("jsonb").IsRequired();
        asset.HasIndex(x => new { x.UserId, x.CreatedAt });
        asset.HasIndex(x => new { x.IsPublic, x.IsArchived, x.CreatedAt });
        asset.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        asset.HasOne(x => x.SourceProject).WithMany().HasForeignKey(x => x.SourceProjectId).OnDelete(DeleteBehavior.SetNull);

        var assignment = builder.Entity<WallpaperAssignment>();
        assignment.ToTable("wallpaper_assignments");
        assignment.Property(x => x.Target).HasConversion<string>().HasMaxLength(20);
        assignment.Property(x => x.Observation).HasConversion<string>().HasMaxLength(20);
        assignment.HasIndex(x => new { x.DeviceDisplayId, x.Target }).IsUnique();
        assignment.HasOne(x => x.DeviceDisplay).WithMany().HasForeignKey(x => x.DeviceDisplayId).OnDelete(DeleteBehavior.Cascade);
        assignment.HasOne<WallpaperAsset>().WithMany().HasForeignKey(x => x.DesiredAssetId).OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<WallpaperAsset>().WithMany().HasForeignKey(x => x.ReportedAssetId).OnDelete(DeleteBehavior.Restrict);

        var command = builder.Entity<DeviceCommand>();
        command.ToTable("device_commands");
        command.Property(x => x.Target).HasConversion<string>().HasMaxLength(20);
        command.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        command.Property(x => x.Error).HasMaxLength(1000);
        command.HasIndex(x => new { x.DeviceId, x.RequestId }).IsUnique();
        command.HasIndex(x => new { x.DeviceId, x.Sequence }).IsUnique();
        command.HasIndex(x => new { x.DeviceId, x.Status, x.ExpiresAt });
        command.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        command.HasOne(x => x.DeviceDisplay).WithMany().HasForeignKey(x => x.DeviceDisplayId).OnDelete(DeleteBehavior.Cascade);
        command.HasOne(x => x.WallpaperAsset).WithMany().HasForeignKey(x => x.WallpaperAssetId).OnDelete(DeleteBehavior.Restrict);
    }

    private void ApplyUtcTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
