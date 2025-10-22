namespace LucidForums.Services.Translation;

public interface IContentTranslationQueue
{
    /// <summary>
    /// Queue a message for translation into all available languages
    /// </summary>
    void QueueMessageTranslation(Guid messageId, string content);

    /// <summary>
    /// Queue a specific field of content for translation
    /// </summary>
    void QueueContentTranslation(string contentType, string contentId, string fieldName, string content);
}
