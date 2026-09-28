using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Gallery;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/comments")]
public sealed class CommentsController(IGalleryService galleryService) : ControllerBase
{
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await galleryService.DeleteCommentAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }
}
