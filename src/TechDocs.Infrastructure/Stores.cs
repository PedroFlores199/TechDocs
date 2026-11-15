using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson;
using MongoDB.Driver;
using TechDocs.Application;
using TechDocs.Domain;

namespace TechDocs.Infrastructure;

public sealed class MongoContentStore : IContentStore
{
    private readonly IMongoCollection<VersionContent> collection;
    static MongoContentStore()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }
    public MongoContentStore(string connection)
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(VersionContent)))
        {
            BsonClassMap.RegisterClassMap<VersionContent>(m =>
            {
                m.AutoMap();
                m.MapMember(x => x.DocumentId).SetSerializer(new GuidSerializer(GuidRepresentation.Standard));
            });
        }
        collection = new MongoClient(connection).GetDatabase("techdocs").GetCollection<VersionContent>("versions");
    }
    public Task SaveAsync(VersionContent content, CancellationToken ct) => collection.ReplaceOneAsync(x => x.Id == content.Id, content, new ReplaceOptions { IsUpsert = true }, ct);
    public async Task<VersionContent?> ReadAsync(Guid versionId, CancellationToken ct) => await collection.Find(x => x.Id == versionId.ToString()).FirstOrDefaultAsync(ct);
}
public sealed class FileContentStore(string directory) : IContentStore
{
    public async Task SaveAsync(VersionContent content, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, content.Id + ".json");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(content), ct);
        File.Move(temporary, path, true);
    }
    public async Task<VersionContent?> ReadAsync(Guid versionId, CancellationToken ct)
    {
        var path = Path.Combine(directory, versionId + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<VersionContent>(await File.ReadAllTextAsync(path, ct)) : null;
    }
}
public sealed class ElasticsearchIndex(HttpClient http) : ISearchIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task InitializeAsync(CancellationToken ct)
    {
        var exists = await http.GetAsync("techdocs", ct);
        if (exists.IsSuccessStatusCode) return;
        if (exists.StatusCode != System.Net.HttpStatusCode.NotFound) exists.EnsureSuccessStatusCode();
        var response = await http.PutAsJsonAsync("techdocs", new
        {
            settings = new { analysis = new { filter = new { technical_synonyms = new { type = "synonym", synonyms = new[] { "junta, sello, anillo", "hidraulico, hidraulica", "filtro, filtracion" } } }, analyzer = new { technical = new { tokenizer = "standard", filter = new[] { "lowercase", "asciifolding", "technical_synonyms" } } } } },
            mappings = new { properties = new { title = new { type = "text", analyzer = "technical" }, text = new { type = "text", analyzer = "technical" }, code = new { type = "keyword" }, state = new { type = "keyword" }, systemId = new { type = "keyword" }, updatedAt = new { type = "date" } } }
        }, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.BadRequest) response.EnsureSuccessStatusCode();
    }
    public async Task IndexAsync(SearchHit hit, string text, CancellationToken ct)
    {
        var r = await http.PutAsJsonAsync($"techdocs/_doc/{hit.Id}", new { hit.Id, hit.Code, hit.Title, hit.System, hit.Version, hit.State, hit.Excerpt, hit.UpdatedAt, hit.SystemId, text }, Json, ct);
        r.EnsureSuccessStatusCode();
    }
    public async Task<PageResult<SearchHit>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var filters = new List<object>();
        if (request.State is not null) filters.Add(new { term = new { state = request.State } });
        if (request.SystemId is not null) filters.Add(new { term = new { systemId = request.SystemId.ToString() } });
        if (request.From is not null || request.To is not null) filters.Add(new { range = new { updatedAt = new { gte = request.From, lte = request.To } } });
        object must = string.IsNullOrWhiteSpace(request.Query) ? new { match_all = new { } } : new { multi_match = new { query = request.Query, fields = new[] { "title^3", "text", "code^5" }, fuzziness = "AUTO", @operator = "and" } };
        var r = await http.PostAsJsonAsync("techdocs/_search", new { from = (request.Page - 1) * request.PageSize, size = request.PageSize, query = new { @bool = new { must, filter = filters } }, sort = string.IsNullOrWhiteSpace(request.Query) ? new object[] { new { updatedAt = "desc" } } : new object[] { "_score" } }, ct);
        r.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var hits = json.RootElement.GetProperty("hits");
        return new(hits.GetProperty("hits").EnumerateArray().Select(x => x.GetProperty("_source").Deserialize<SearchHit>(Json)!).ToArray(), hits.GetProperty("total").GetProperty("value").GetInt32(), request.Page, request.PageSize);
    }
}
public sealed class LocalSearchIndex(TechDocsDb db, IContentStore content) : ISearchIndex
{
    public Task IndexAsync(SearchHit hit, string text, CancellationToken ct) => Task.CompletedTask;
    public async Task<PageResult<SearchHit>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var docs = await db.Documents.Include(d => d.System).Include(d => d.Versions).AsNoTracking().ToListAsync(ct);
        var terms = Normalize(request.Query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<SearchHit>();
        foreach (var d in docs)
        {
            var v = d.Versions.Single(x => x.Number == d.CurrentVersion);
            if (request.State is not null && v.State.ToString() != request.State || request.SystemId is not null && d.SystemId != request.SystemId || request.From is not null && v.UpdatedAt < request.From || request.To is not null && v.UpdatedAt > request.To) continue;
            var body = await content.ReadAsync(v.Id, ct);
            var text = string.Join(' ', body?.Blocks.Select(x => x.Text) ?? []);
            var haystack = Normalize(d.Title + " " + d.Code + " " + text);
            if (!terms.All(haystack.Contains)) continue;
            hits.Add(new(d.Id, d.Code, d.Title, d.System.Name, v.Number, v.State.ToString(), text[..Math.Min(200, text.Length)], v.UpdatedAt, d.SystemId));
        }
        var sorted = hits.OrderByDescending(x => x.UpdatedAt).ToArray();
        return new(sorted.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArray(), sorted.Length, request.Page, request.PageSize);
    }
    private static string Normalize(string s) => string.Concat(s.Normalize(System.Text.NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
}
