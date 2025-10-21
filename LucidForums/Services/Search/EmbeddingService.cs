using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LucidForums.Data;
using LucidForums.Models.Entities;
using LucidForums.Services.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace LucidForums.Services.Search;

using LucidForums.Helpers;

public class EmbeddingOptions : IConfigSection
{
    public static string Section => "Embedding";
    public string? Model { get; set; } = "mxbai-embed-large"; // sensible default for Ollama
    // Provider can be "ollama" (default) or "lmstudio" (OpenAI-compatible)
    public string Provider { get; set; } = "ollama";
    // When using LMStudio, base endpoint (e.g., http://localhost:1234). If null, defaults to localhost:1234
    public string? Endpoint { get; set; }
}

public class EmbeddingService(ApplicationDbContext db, IHttpClientFactory httpFactory, IOllamaEndpointProvider ollama, Microsoft.Extensions.Options.IOptionsMonitor<EmbeddingOptions> options, Microsoft.Extensions.Options.IOptionsMonitor<LucidForums.Services.Ai.AiOptions> aiOptions, IServiceProvider serviceProvider) : IEmbeddingService
{
    private readonly ApplicationDbContext _db = db;
    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly IOllamaEndpointProvider _ollama = ollama;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<EmbeddingOptions> _embOptions = options;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<LucidForums.Services.Ai.AiOptions> _aiOptions = aiOptions;
    private readonly IServiceProvider _sp = serviceProvider;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var opts = _embOptions.CurrentValue;
        var model = string.IsNullOrWhiteSpace(opts.Model) ? "mxbai-embed-large" : opts.Model!;
        var provider = (opts.Provider ?? "ollama").Trim().ToLowerInvariant();

        // Prefer Microsoft.Extensions.AI embedding generator if available (for OpenAI-compatible providers like LM Studio/OpenAI)
        var genObj = _sp.GetService(typeof(IEmbeddingGenerator<string, Embedding<float>>));
        if (genObj is not null && (provider == "openai" || provider == "lmstudio"))
        {
            try
            {
                dynamic dynGen = genObj;
                try
                {
                    dynamic embedding = await dynGen.GenerateEmbeddingAsync(text, ct);
                    var vecObj = embedding.Vector;
                    if (vecObj is float[] arr) return arr;
                    if (vecObj is ReadOnlyMemory<float> rom) return rom.ToArray();
                    if (vecObj is Memory<float> mem) return mem.ToArray();
                    if (vecObj is IReadOnlyList<float> list) return list.ToArray();
                    if (vecObj is IEnumerable<float> en) return en.ToArray();
                }
                catch
                {
                    dynamic embedding = await dynGen.GenerateAsync(text, ct);
                    var vecObj = embedding.Vector;
                    if (vecObj is float[] arr) return arr;
                    if (vecObj is ReadOnlyMemory<float> rom) return rom.ToArray();
                    if (vecObj is Memory<float> mem) return mem.ToArray();
                    if (vecObj is IReadOnlyList<float> list) return list.ToArray();
                    if (vecObj is IEnumerable<float> en) return en.ToArray();
                }
            }
            catch
            {
                // fall back to HTTP flow below
            }
        }

        var client = _httpFactory.CreateClient("ollama");

        Uri requestUri;
        object payload;

        if (provider == "lmstudio")
        {
            var baseUrl = string.IsNullOrWhiteSpace(opts.Endpoint) ? "http://localhost:1234" : opts.Endpoint!;
            if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) baseUrl = "http://" + baseUrl;
            var baseUri = new Uri(baseUrl);
            requestUri = new Uri(baseUri, "/v1/embeddings");
            payload = new { model, input = text };
        }
        else if (provider == "openai")
        {
            var baseUrl = string.IsNullOrWhiteSpace(opts.Endpoint) ? "https://api.openai.com" : opts.Endpoint!;
            var baseUri = new Uri(baseUrl);
            requestUri = new Uri(baseUri, "/v1/embeddings");
            payload = new { model, input = text };
        }
        else
        {
            requestUri = new Uri(_ollama.GetBaseAddress(), "/api/embeddings");
            payload = new { model, input = text };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (provider == "openai")
        {
            var ai = _aiOptions.CurrentValue;
            var key = ai.ApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            }
        }
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("embedding", out var embEl) && embEl.ValueKind == JsonValueKind.Array)
        {
            return embEl.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
        }
        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
        {
            // Some servers return { data: [{ embedding: [...] }] }
            var first = dataEl.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("embedding", out var emb2))
            {
                return emb2.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            }
        }
        throw new InvalidOperationException("Embedding response did not contain an embedding array");
    }

    public async Task IndexMessageAsync(Guid messageId, CancellationToken ct = default)
    {
        var msg = await _db.Messages.AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => new { m.Id, m.ForumThreadId, m.Content })
            .FirstOrDefaultAsync(ct);
        if (msg == null) return;
        var thread = await _db.Threads.AsNoTracking().Where(t => t.Id == msg.ForumThreadId).Select(t => new { t.Id, t.ForumId }).FirstOrDefaultAsync(ct);
        if (thread == null) return;

        var hash = ComputeHash(msg.Content);

        // check if already indexed with same hash
        var existingHash = await _db.Database.ExecuteSqlRawAsync("SELECT 1 WHERE EXISTS (SELECT 1 FROM message_embeddings WHERE message_id = {0} AND content_hash = {1})", msg.Id, hash);
        if (existingHash == 1) return;

        var emb = await EmbedAsync(msg.Content, ct);
        var embLiteral = "[" + string.Join(",", emb.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

        // Upsert
        var sql = @"INSERT INTO message_embeddings (message_id, forum_id, thread_id, content_hash, embedding, created_at)
VALUES ({0}, {1}, {2}, {3}, {4}::vector, now())
ON CONFLICT (message_id) DO UPDATE SET content_hash = excluded.content_hash, embedding = excluded.embedding, created_at = now();";
        await _db.Database.ExecuteSqlRawAsync(sql, msg.Id, thread.ForumId, thread.Id, hash, embLiteral);
    }

    public async Task<List<(Guid MessageId, double Score)>> SearchAsync(string query, Guid? forumId, int limit = 10, CancellationToken ct = default)
    {
        var emb = await EmbedAsync(query, ct);
        var embLiteral = "[" + string.Join(",", emb.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";
        string sql;
        if (forumId.HasValue)
        {
            sql = @"SELECT message_id, (embedding <=> {0}::vector) AS score FROM message_embeddings WHERE forum_id = {1} ORDER BY embedding <=> {0}::vector ASC LIMIT {2}";
            var rows = await _db.Set<SearchRow>().FromSqlRaw(sql, embLiteral, forumId.Value, limit).ToListAsync(ct);
            return rows.Select(r => (r.message_id, r.score)).ToList();
        }
        else
        {
            sql = @"SELECT message_id, (embedding <=> {0}::vector) AS score FROM message_embeddings ORDER BY embedding <=> {0}::vector ASC LIMIT {1}";
            var rows = await _db.Set<SearchRow>().FromSqlRaw(sql, embLiteral, limit).ToListAsync(ct);
            return rows.Select(r => (r.message_id, r.score)).ToList();
        }
    }

    private static string ComputeHash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    // Helper type for raw SQL mapping
    private class SearchRow
    {
        public Guid message_id { get; set; }
        public double score { get; set; }
    }
}
