using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;

namespace UpscaleLab.Infrastructure.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<OptimizedImage> OptimizedImages => Set<OptimizedImage>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<GalleryPost> GalleryPosts => Set<GalleryPost>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<GalleryLike> GalleryLikes => Set<GalleryLike>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureDevice(modelBuilder);
        ConfigureImage(modelBuilder);
        ConfigureOptimizedImage(modelBuilder);
        ConfigureUserSetting(modelBuilder);
        ConfigureGallery(modelBuilder);
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
        entity.HasOne(x => x.User).WithOne(x => x.Setting).HasForeignKey<UserSetting>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureGallery(ModelBuilder modelBuilder)
    {
        var post = modelBuilder.Entity<GalleryPost>();
        post.ToTable("gallery_posts");
        post.Property(x => x.Title).HasMaxLength(160).IsRequired();
        post.Property(x => x.Description).HasMaxLength(2000);
        post.HasIndex(x => x.CreatedAt);
        post.HasOne(x => x.User).WithMany(x => x.GalleryPosts).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        post.HasOne(x => x.Image).WithMany(x => x.GalleryPosts).HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Cascade);

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
