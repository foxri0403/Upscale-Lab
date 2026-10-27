using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Wallpapers;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize(Policy = "AccountSession")]
[Route("api/wallpapers")]
public sealed class WallpapersController(IWallpaperService wallpapers) : ControllerBase
{
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await wallpapers.ArchiveAsync(User.GetRequiredUserId(), id, ct);
        return NoContent();
    }
}
