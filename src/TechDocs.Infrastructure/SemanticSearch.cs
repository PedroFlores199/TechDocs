using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TechDocs.Application;

namespace TechDocs.Infrastructure;

public sealed class EmbeddingClient(HttpClient http, IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, float[]> cache = new();
    public bool Enabled => configuration.GetValue<bool>("Embeddings:Enabled");
    public string Model => configuration["Embeddings:Model"] ?? "nomic-embed-text";
    public async Task<float[]> EmbedAsync(string key, string text, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out var result)) return result;
        var response = await http.PostAsJsonAsync((configuration["Embeddings:Url"] ?? "http://localhost:11434") + "/api/embed", new { model = Model, input = text, truncate = true }, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var vector = json.RootElement.GetProperty("embeddings")[0].EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if (cache.Count > 10000) cache.Clear();
        cache[key] = vector;
        return vector;
    }
}
public sealed class SemanticSearch(TechDocsDb db, DocumentService documents, EmbeddingClient embeddings)
{
    public async Task<object> AskAsync(string question, CancellationToken ct)
    {
        if (!embeddings.Enabled) throw new DomainException("Activa el perfil ai de Docker Compose para usar la búsqueda semántica.", 503);
        if (string.IsNullOrWhiteSpace(question) || question.Length > 1000) throw new DomainException("La pregunta debe tener entre 1 y 1.000 caracteres.");
        var query = await embeddings.EmbedAsync("query:" + question, question, ct);
        var docs = await db.Documents.Include(x => x.Versions).AsNoTracking().ToListAsync(ct);
        var hits = new List<object>(); var scores = new List<(double Score, object Value)>();
        foreach (var d in docs)
        {
            var v = d.Versions.Single(x => x.Number == d.CurrentVersion);
            var body = await documents.ReadContentAsync(v, ct);
            for (var i = 0; i < body.Blocks.Count; i++)
            {
                var b = body.Blocks[i];
                if (b.Type != "step") continue;
                var vector = await embeddings.EmbedAsync($"{v.Id}:{v.Hash}:{i}", d.Title + "\n" + b.Text, ct);
                var score = Cosine(query, vector);
                scores.Add((score, new { documentId = d.Id, d.Code, d.Title, version = v.Number, step = body.Blocks.Take(i + 1).Count(x => x.Type == "step"), citation = b.Text, score }));
            }
        }
        hits.AddRange(scores.OrderByDescending(x => x.Score).Take(5).Select(x => x.Value));
        return new { question, model = embeddings.Model, results = hits, note = "Recuperación por embeddings. Cada resultado cita texto del procedimiento; no se generan instrucciones técnicas." };
    }
    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; normA += a[i] * a[i]; normB += b[i] * b[i]; }
        return normA == 0 || normB == 0 ? 0 : dot / Math.Sqrt(normA * normB);
    }
}
