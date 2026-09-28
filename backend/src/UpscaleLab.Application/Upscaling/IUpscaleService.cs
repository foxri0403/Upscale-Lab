namespace UpscaleLab.Application.Upscaling;

public sealed record UpscaleRequest(string InputUrl, int Scale = 4, string Model = "nightmareai/real-esrgan");

public sealed record UpscalePrediction(string Id, string Status, string? OutputUrl, string? Error);

public interface IUpscaleService
{
    Task<UpscalePrediction> CreatePredictionAsync(UpscaleRequest request, CancellationToken cancellationToken);
    Task<UpscalePrediction> GetPredictionAsync(string predictionId, CancellationToken cancellationToken);
}
