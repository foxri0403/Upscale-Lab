using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Models;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController(
    IProjectService projectService,
    IProjectProcessingService processingService) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<ProjectDetailResponse>> Create(
        [FromForm] CreateProjectForm form,
        CancellationToken cancellationToken)
    {
        if (form.File.Length <= 0)
        {
            return ValidationProblem("빈 파일은 업로드할 수 없습니다.");
        }

        if (!AllowedContentTypes.Contains(form.File.ContentType))
        {
            ModelState.AddModelError(nameof(form.File), "JPEG, PNG, WebP 이미지만 업로드할 수 있습니다.");
            return ValidationProblem(ModelState);
        }

        await using var stream = form.File.OpenReadStream();
        var response = await projectService.CreateAsync(
            User.GetRequiredUserId(),
            new CreateProjectCommand(
                stream,
                form.File.FileName,
                form.File.ContentType,
                form.Title,
                form.OriginalWidth,
                form.OriginalHeight),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { projectId = response.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectSummaryResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await projectService.GetAllAsync(User.GetRequiredUserId(), cancellationToken));

    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult<ProjectDetailResponse>> Get(Guid projectId, CancellationToken cancellationToken) =>
        Ok(await projectService.GetAsync(User.GetRequiredUserId(), projectId, cancellationToken));

    [HttpDelete("{projectId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, CancellationToken cancellationToken)
    {
        await projectService.DeleteAsync(User.GetRequiredUserId(), projectId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{projectId:guid}/process")]
    public async Task<ActionResult<ProcessingStatusResponse>> Process(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var response = await processingService.StartAsync(User.GetRequiredUserId(), projectId, cancellationToken);
        return Accepted(response);
    }

    [HttpGet("{projectId:guid}/processing-status")]
    public async Task<ActionResult<ProcessingStatusResponse>> GetProcessingStatus(
        Guid projectId,
        CancellationToken cancellationToken) =>
        Ok(await processingService.GetStatusAsync(User.GetRequiredUserId(), projectId, cancellationToken));

    [HttpGet("{projectId:guid}/layers")]
    public async Task<ActionResult<IReadOnlyList<LayerResponse>>> GetLayers(
        Guid projectId,
        CancellationToken cancellationToken) =>
        Ok(await projectService.GetLayersAsync(User.GetRequiredUserId(), projectId, cancellationToken));

    [HttpPatch("{projectId:guid}/layers/{layerId:guid}")]
    public async Task<ActionResult<LayerResponse>> UpdateLayer(
        Guid projectId,
        Guid layerId,
        UpdateLayerRequest request,
        CancellationToken cancellationToken) =>
        Ok(await projectService.UpdateLayerAsync(
            User.GetRequiredUserId(),
            projectId,
            layerId,
            request,
            cancellationToken));
}
