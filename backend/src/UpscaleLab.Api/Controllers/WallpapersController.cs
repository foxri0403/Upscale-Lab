using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize(Policy = "AccountSession")]
[Route("api/wallpapers")]
public sealed class WallpapersController(IWallpaperService wallpapers) : ControllerBase
{
    [HttpGet("{id:guid}/content/{order:int}")]
    public async Task<IActionResult> Content(Guid id, int order, CancellationToken ct)
    {
        var content = await wallpapers.ReadContentAsync(User.GetRequiredUserId(), id, order, ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(content.Bytes, content.ContentType);
    }

    [AllowAnonymous]
    [HttpGet("links/{token}/content/{order:int}")]
    public async Task<IActionResult> LinkedContent(string token, int order, CancellationToken ct)
    {
        var content = await wallpapers.ReadLinkedContentAsync(token, order, ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(content.Bytes, content.ContentType);
    }
    [HttpPost("{id:guid}/share-link")]
    public async Task<ActionResult<WallpaperLink>> CreateLink(Guid id, CancellationToken ct) =>
        Ok(await wallpapers.CreateLinkAsync(User.GetRequiredUserId(), id, ct));

    [AllowAnonymous]
    [HttpGet("links/{token}")]
    public async Task<ActionResult<LinkedWallpaper>> Linked(string token, CancellationToken ct) =>
        Ok(await wallpapers.GetLinkedAsync(token, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WallpaperSummary>>> List(CancellationToken ct) =>
        Ok(await wallpapers.ListAsync(User.GetRequiredUserId(), false, ct));

    [AllowAnonymous]
    [HttpGet("shared")]
    public async Task<ActionResult<IReadOnlyList<WallpaperSummary>>> Shared(CancellationToken ct) =>
        Ok(await wallpapers.ListAsync(null, true, ct));

    [HttpPost]
    public async Task<ActionResult<WallpaperSummary>> Create(CreateWallpaperRequest request, CancellationToken ct)
    {
        var asset = await wallpapers.CreateAsync(User.GetRequiredUserId(), request, ct);
        return Created($"/api/wallpapers/{asset.Id}/manifest", asset);
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/manifest")]
    public async Task<ActionResult<WallpaperManifest>> Manifest(Guid id, CancellationToken ct) =>
        Ok(await wallpapers.GetManifestAsync(User.GetOptionalUserId(), id, false, ct));

    [HttpPost("{id:guid}/import")]
    public async Task<ActionResult<WallpaperSummary>> Import(Guid id, CancellationToken ct)
    {
        var asset = await wallpapers.ImportAsync(User.GetRequiredUserId(), id, ct);
        return Created($"/api/wallpapers/{asset.Id}/manifest", asset);
    }

    [HttpPost("{id:guid}/snapshot")]
    public async Task<ActionResult<WallpaperSummary>> Snapshot(Guid id, SnapshotWallpaperRequest request, CancellationToken ct)
    {
        var asset = await wallpapers.SnapshotAsync(User.GetRequiredUserId(), id, request, ct);
        return Created($"/api/wallpapers/{asset.Id}/manifest", asset);
    }

    [HttpPut("{id:guid}/sharing")]
    public async Task<IActionResult> Sharing(Guid id, ShareWallpaperRequest request, CancellationToken ct)
    {
        await wallpapers.SetSharingAsync(User.GetRequiredUserId(), id, request.IsPublic, ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, RenameWallpaperRequest request, CancellationToken ct)
    {
        await wallpapers.RenameAsync(User.GetRequiredUserId(), id, request, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/edit")]
    public async Task<ActionResult<ImageEditState>> SaveEdit(Guid id, SaveImageEditRequest request, CancellationToken ct) =>
        Ok(await wallpapers.SaveEditAsync(User.GetRequiredUserId(), id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await wallpapers.ArchiveAsync(User.GetRequiredUserId(), id, ct);
        return NoContent();
    }
}
