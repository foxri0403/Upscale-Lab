using System.Threading.Channels;
using UpscaleLab.Application.Processing;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class ProcessingJobQueue : IProcessingJobQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false, // Multiple LiveLayerProcessingWorker consumers read concurrently.
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) =>
        channel.Writer.WriteAsync(jobId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAsync(cancellationToken);
}
