using System.Threading.Channels;

namespace LucidForums.Services.Translation;

public record TranslationQueueItem(string ContentType, string ContentId, string FieldName, string Content);

/// <summary>
/// Channel-based queue for background content translation
/// </summary>
public class ContentTranslationQueue : IContentTranslationQueue
{
    private readonly Channel<TranslationQueueItem> _channel;

    public ContentTranslationQueue()
    {
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _channel = Channel.CreateBounded<TranslationQueueItem>(options);
    }

    public void QueueMessageTranslation(Guid messageId, string content)
    {
        QueueContentTranslation("Message", messageId.ToString(), "Content", content);
    }

    public void QueueContentTranslation(string contentType, string contentId, string fieldName, string content)
    {
        var item = new TranslationQueueItem(contentType, contentId, fieldName, content);
        _channel.Writer.TryWrite(item);
    }

    public ChannelReader<TranslationQueueItem> Reader => _channel.Reader;
}
