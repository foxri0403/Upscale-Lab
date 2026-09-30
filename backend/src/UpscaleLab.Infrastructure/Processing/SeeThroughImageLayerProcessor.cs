using System.Diagnostics;
using System.Text.Json;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class SeeThroughImageLayerProcessor(SeeThroughOptions options) : IImageLayerProcessor
{
    public async Task<IReadOnlyList<ProcessedLayer>> ProcessAsync(
        LayerProcessingRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        Directory.CreateDirectory(request.OutputDirectory);
        var manifestPath = Path.Combine(request.OutputDirectory, "manifest.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = options.PythonExecutable,
            WorkingDirectory = options.RepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(options.AdapterScriptPath);
        startInfo.ArgumentList.Add("--repository");
        startInfo.ArgumentList.Add(options.RepositoryPath);
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(request.InputPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(request.OutputDirectory);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("See-through 처리 프로세스를 시작할 수 없습니다.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(options.TimeoutMinutes, 1, 240)));
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"See-through 처리가 {options.TimeoutMinutes}분 제한을 초과했습니다.");
        }

        var stdout = await standardOutput;
        var stderr = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"See-through 처리 실패(exit {process.ExitCode}): {Truncate(stderr, 1500)}");
        }

        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"See-through 결과 manifest가 없습니다. {Truncate(stdout, 500)}");
        }

        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<AdapterManifest>(
            manifestStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? throw new InvalidOperationException("See-through 결과 manifest를 읽을 수 없습니다.");

        var outputRoot = Path.GetFullPath(request.OutputDirectory) + Path.DirectorySeparatorChar;
        var layers = new List<ProcessedLayer>();
        foreach (var item in manifest.Layers.Take(64))
        {
            var filePath = Path.GetFullPath(Path.Combine(request.OutputDirectory, item.File));
            if (!filePath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            {
                throw new InvalidOperationException("See-through manifest에 잘못된 레이어 경로가 포함되어 있습니다.");
            }

            var layerType = Enum.TryParse<LayerType>(item.LayerType, true, out var parsed)
                ? parsed
                : LayerType.Other;
            layers.Add(new ProcessedLayer(
                filePath,
                layerType,
                item.LayerOrder,
                item.Depth,
                item.PositionX,
                item.PositionY,
                item.Rotation,
                item.Scale <= 0 ? 1 : item.Scale,
                Math.Max(0, item.MovementX),
                Math.Max(0, item.MovementY)));
        }

        return layers.OrderBy(x => x.LayerOrder).ToList();
    }

    private void EnsureConfigured()
    {
        if (!options.Enabled)
        {
            throw new ConfigurationException("See-through 처리가 비활성화되어 있습니다. SeeThrough:Enabled를 확인하세요.");
        }

        if (string.IsNullOrWhiteSpace(options.RepositoryPath) || !Directory.Exists(options.RepositoryPath))
        {
            throw new ConfigurationException("SeeThrough:RepositoryPath가 올바른 저장소를 가리켜야 합니다.");
        }

        if (string.IsNullOrWhiteSpace(options.AdapterScriptPath) || !File.Exists(options.AdapterScriptPath))
        {
            throw new ConfigurationException("SeeThrough:AdapterScriptPath가 올바른 어댑터를 가리켜야 합니다.");
        }

        var inferenceScript = Path.Combine(options.RepositoryPath, "inference", "scripts", "inference_psd.py");
        if (!File.Exists(inferenceScript))
        {
            throw new ConfigurationException("See-through 저장소에서 inference/scripts/inference_psd.py를 찾을 수 없습니다.");
        }
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private sealed record AdapterManifest(IReadOnlyList<AdapterLayer> Layers);

    private sealed record AdapterLayer(
        string File,
        string LayerType,
        int LayerOrder,
        double Depth,
        double PositionX,
        double PositionY,
        double Rotation,
        double Scale,
        double MovementX,
        double MovementY);
}
