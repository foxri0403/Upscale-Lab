namespace UpscaleLab.Domain.Entities;

public sealed class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsEmailVerified { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public string? EmailVerificationCodeHash { get; set; }
    public DateTime? EmailVerificationCodeExpiresAt { get; set; }
    public DateTime? EmailVerificationCodeSentAt { get; set; }
    public int EmailVerificationFailedAttempts { get; set; }

    public UserSetting? Setting { get; set; }
    public ICollection<Device> Devices { get; set; } = new List<Device>();
    public ICollection<Image> Images { get; set; } = new List<Image>();
    public ICollection<LiveLayerProject> Projects { get; set; } = new List<LiveLayerProject>();
    public ICollection<GalleryPost> GalleryPosts { get; set; } = new List<GalleryPost>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<GalleryLike> Likes { get; set; } = new List<GalleryLike>();
}
