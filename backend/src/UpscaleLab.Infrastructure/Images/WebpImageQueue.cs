using System.Threading.Channels;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Infrastructure.Images;

public readonly record struct WebpImageWork(WebpImageKind Kind, Guid Id);

public sealed class WebpImageQueue
{
    private readonly Channel<WebpImageWork> pending = Channel.CreateBounded<WebpImageWork>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });

    public void Schedule(WebpImageKind kind, Guid id) => pending.Writer.TryWrite(new(kind, id));

    public IAsyncEnumerable<WebpImageWork> ReadAllAsync(CancellationToken ct) => pending.Reader.ReadAllAsync(ct);
}
