using Microsoft.Extensions.Options;

namespace LucidForums.Services.Llm;

public interface IOllamaEndpointProvider
{
    Uri GetBaseAddress();
    OllamaOptions Options { get; }
}

public class OllamaEndpointProvider(IOptions<OllamaOptions> options) : IOllamaEndpointProvider
{
    private readonly OllamaOptions _options = options.Value;
    private Uri? _baseUri;

    public Uri GetBaseAddress()
    {
        if (_baseUri != null) return _baseUri;

        var endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? (Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434")
            : _options.Endpoint;

        _baseUri = new Uri(endpoint);
        return _baseUri;
    }

    public OllamaOptions Options => _options;
}