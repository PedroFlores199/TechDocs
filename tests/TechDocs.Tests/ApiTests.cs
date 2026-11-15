using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TechDocs.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "techdocs-tests-" + Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { { "Storage:Mode", "Local" }, { "Storage:Directory", Path.Combine(directory, "versions") }, { "ConnectionStrings:Sqlite", "Data Source=" + Path.Combine(directory, "test.db") + ";Pooling=False" } }));
    }
    public async Task<HttpClient> Login(string role)
    {
        var client = CreateClient(); var response = await client.PostAsJsonAsync("/api/auth/login", new { email = role.ToLowerInvariant() + "@techdocs.demo", password = "Demo1234!" }); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString()); return client;
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing && Directory.Exists(directory)) Directory.Delete(directory, true); }
}
public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task BrakingComponentsBelongToTheBrakingSystem()
    {
        using var client = await factory.Login("Lector");
        var result = await client.GetFromJsonAsync<JsonElement>("/api/parts?q=Disco");
        var items = result.GetProperty("items").EnumerateArray().ToArray();
        items.Should().NotBeEmpty();
        items.All(x => x.GetProperty("system").GetString() == "Frenado").Should().BeTrue();
    }
    [Fact] public async Task AuthenticationIsRequired() { var r = await factory.CreateClient().GetAsync("/api/documents"); r.StatusCode.Should().Be(HttpStatusCode.Unauthorized); }
    [Fact] public async Task ReaderCannotCreateDocuments() { using var c = await factory.Login("Lector"); var r = await c.PostAsJsonAsync("/api/documents", new { code = "MP-FAIL", title = "Test", systemId = Guid.NewGuid(), blocks = new[] { new { type = "step", text = "Hello" } }, partIds = Array.Empty<Guid>() }); r.StatusCode.Should().Be(HttpStatusCode.Forbidden); }
    [Fact] public async Task SearchIgnoresAccents() { using var c = await factory.Login("Lector"); var json = await c.GetFromJsonAsync<JsonElement>("/api/documents?q=junta%20torica"); json.GetProperty("total").GetInt32().Should().BeGreaterThan(0); }
    [Fact] public async Task PdfContainsRealPdfHeader() { using var c = await factory.Login("Lector"); var r = await c.GetAsync("/api/manual.pdf"); r.EnsureSuccessStatusCode(); var bytes = await r.Content.ReadAsByteArrayAsync(); System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-"); bytes.Length.Should().BeGreaterThan(3000); }
    [Fact] public async Task InvalidReferenceReturnsProblemDetails() { using var c = await factory.Login("Redactor"); var r = await c.PostAsJsonAsync("/api/documents", new { code = "MP-NONE", title = "Test", systemId = Guid.NewGuid(), blocks = new[] { new { type = "step", text = "Example" } }, partIds = Array.Empty<Guid>() }); r.StatusCode.Should().Be(HttpStatusCode.BadRequest); r.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json"); }
    [Fact]
    public async Task PartChangeCreatesImpactTasksAndRejectsStaleRevision()
    {
        using var c = await factory.Login("Redactor"); var list = await c.GetFromJsonAsync<JsonElement>("/api/parts"); var part = list.GetProperty("items")[0]; var id = part.GetProperty("id").GetGuid(); var revision = part.GetProperty("revision").GetInt32();
        var change = new { name = part.GetProperty("name").GetString(), specification = "Especificación revisada en prueba", expectedRevision = revision };
        var r = await c.PutAsJsonAsync("/api/parts/" + id, change); r.EnsureSuccessStatusCode(); var result = await r.Content.ReadFromJsonAsync<JsonElement>(); result.GetProperty("affectedDocuments").GetArrayLength().Should().BeGreaterThan(0);
        var stale = await c.PutAsJsonAsync("/api/parts/" + id, change); stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var tasks = await c.GetFromJsonAsync<JsonElement>("/api/tasks"); tasks.GetArrayLength().Should().BeGreaterThan(0);
    }
    [Fact]
    public async Task NewVersionPreservesPreviousContent()
    {
        using var c = await factory.Login("Redactor"); var systems = await c.GetFromJsonAsync<JsonElement>("/api/systems"); var system = systems[0].GetProperty("id").GetGuid();
        var request = new { code = "MP-TEST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), title = "Test de versiones", systemId = system, blocks = new[] { new { type = "step", text = "Contenido original" } }, partIds = Array.Empty<Guid>() };
        var created = await c.PostAsJsonAsync("/api/documents", request); created.EnsureSuccessStatusCode(); var result = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = result.GetProperty("id").GetGuid();
        var revised = await c.PutAsJsonAsync("/api/documents/" + id, new { request.code, request.title, systemId = system, blocks = new[] { new { type = "step", text = "Contenido nuevo" } }, partIds = Array.Empty<Guid>(), expectedRevision = 1 }); revised.EnsureSuccessStatusCode();
        var old = await c.GetFromJsonAsync<JsonElement>($"/api/documents/{id}?version=1"); old.GetProperty("blocks")[0].GetProperty("text").GetString().Should().Be("Contenido original");
        var latest = await c.GetFromJsonAsync<JsonElement>($"/api/documents/{id}"); latest.GetProperty("version").GetInt32().Should().Be(2);
    }
}
