using System.ComponentModel.DataAnnotations;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Application.Projects;

public sealed record CreateProjectCommand(
    Stream Content,
    [param: Required, MaxLength(255)] string FileName,
    [param: Required, MaxLength(100)] string ContentType,
    [param: Required, MaxLength(160)] string Title,
    [param: Range(1, 32768)] int OriginalWidth,
    [param: Range(1, 32768)] int OriginalHeight);

public sealed record ProjectSummaryResponse(
    Guid Id,
    string Title,
    ProjectStatus Status,
    string OriginalImageUrl,
    int LayerCount,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProjectDetailResponse(
    Guid Id,
    string Title,
    ProjectStatus Status,
    string OriginalImageUrl,
    int OriginalWidth,
    int OriginalHeight,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<LayerResponse> Layers,
    ProcessingStatusResponse? LatestJob);

public sealed record LayerResponse(
    Guid Id,
    Guid ProjectId,
    LayerType LayerType,
    int LayerOrder,
    string ImageUrl,
    double Depth,
    double PositionX,
    double PositionY,
    double Rotation,
    double Scale,
    double MovementX,
    double MovementY,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record UpdateLayerRequest(
    [param: Range(-100, 100)] double? Depth,
    [param: Range(-100000, 100000)] double? PositionX,
    [param: Range(-100000, 100000)] double? PositionY,
    [param: Range(-360, 360)] double? Rotation,
    [param: Range(0.01, 100)] double? Scale,
    [param: Range(0, 10000)] double? MovementX,
    [param: Range(0, 10000)] double? MovementY);

public sealed record ProcessingStatusResponse(
    Guid JobId,
    Guid ProjectId,
    ProcessingJobStatus Status,
    int Progress,
    string? ErrorMessage,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime UpdatedAt);
