using System.Threading.Channels;

namespace LucidForums.Services.Seeding;

public interface IForumSeedingQueue
{
    ValueTask EnqueueAsync(ForumSeedingRequest request, CancellationToken ct = default);
    ChannelReader<ForumSeedingRequest> Reader { get; }
}

public class ForumSeedingQueue : IForumSeedingQueue
{
    private readonly Channel<ForumSeedingRequest> _channel;

    public ForumSeedingQueue()
    {
        // Unbounded to simplify; can be changed to bounded with policy.
        _channel = Channel.CreateUnbounded<ForumSeedingRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(ForumSeedingRequest request, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(request, ct);

    public ChannelReader<ForumSeedingRequest> Reader => _channel.Reader;
}
