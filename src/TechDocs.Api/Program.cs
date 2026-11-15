using Microsoft.AspNetCore.RateLimiting;
using Role = TechDocs.Domain.Role;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;
using TechDocs.Application;
using TechDocs.Domain;
using TechDocs.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, log) =>
{
    log.ReadFrom.Configuration(context.Configuration).WriteTo.Console();
    if (context.Configuration["Seq:Url"] is { Length: > 0 } seq) log.WriteTo.Seq(seq);
});
builder.Services.AddTechDocs(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<DocumentValidator>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenTelemetry().WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
var local = builder.Configuration["Storage:Mode"] == "Local";
builder.Services.AddHangfire(c =>
{
    c.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings();
    if (local) c.UseInMemoryStorage(); else c.UseSqlServerStorage(builder.Configuration.GetConnectionString("SqlServer"));
});
if (local || builder.Configuration.GetValue<bool>("Jobs:Embedded")) builder.Services.AddHangfireServer();
if (!local) builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
var signingKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Configura Jwt__Key con al menos 32 caracteres.");
if (signingKey.Length < 32) throw new InvalidOperationException("Jwt__Key requiere al menos 32 caracteres.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new()
{
    ValidateIssuer = true,
    ValidIssuer = "techdocs",
    ValidateAudience = true,
    ValidAudience = "techdocs-web",
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
    ClockSkew = TimeSpan.FromSeconds(10)
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddFixedWindowLimiter("login", x => { x.PermitLimit = 20; x.Window = TimeSpan.FromMinutes(1); });
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12_000_000);
var app = builder.Build();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    try { await next(); }
    catch (Exception e) when (e is DomainException or ValidationException or DbUpdateConcurrencyException)
    {
        var status = e is DomainException d ? d.Status : e is DbUpdateConcurrencyException ? 409 : 400;
        await Results.Problem(statusCode: status, title: e is DbUpdateConcurrencyException ? "Otro usuario ha cambiado el registro." : e.Message, extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
});
app.UseRateLimiter();
app.UseAuthentication(); app.UseAuthorization(); app.UseSwagger(); app.UseSwaggerUI();
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (TechDocsDb db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready", mode = local ? "local" : "full" }) : Results.StatusCode(503));
app.MapPost("/api/auth/login", async (LoginRequest r, TechDocsDb db, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(r.Email) || string.IsNullOrEmpty(r.Password) || r.Email.Length > 254 || r.Password.Length > 128) return Results.Unauthorized();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == r.Email.Trim().ToLowerInvariant(), ct);
    if (user is null || !Setup.VerifyPassword(r.Password, user.PasswordHash)) return Results.Unauthorized();
    var expires = DateTime.UtcNow.AddHours(1);
    var token = new JwtSecurityToken("techdocs", "techdocs-web", [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role.ToString()), new Claim(ClaimTypes.Name, user.Name)], expires: expires, signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expires, user = new { user.Id, user.Name, Role = user.Role.ToString() } });
}).RequireRateLimiting("login");
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/dashboard", async (TechDocsDb db, CancellationToken ct) =>
{
    var versions = await db.Documents.Select(d => d.Versions.Single(v => v.Number == d.CurrentVersion).State).ToListAsync(ct);
    return Results.Ok(new { documents = versions.Count, approved = versions.Count(x => x == DocumentState.Approved), inReview = versions.Count(x => x == DocumentState.InReview), drafts = versions.Count(x => x == DocumentState.Draft), parts = await db.Parts.CountAsync(ct), undocumented = await db.Parts.CountAsync(p => !db.DocumentParts.Any(d => d.PartId == p.Id), ct), openTasks = await db.ReviewTasks.CountAsync(x => !x.Completed, ct), pendingSync = await db.Outbox.CountAsync(x => x.ProcessedAt == null, ct), recent = await db.Audit.OrderByDescending(x => x.Id).Take(6).ToListAsync(ct), imports = await db.Imports.CountAsync(ct), mode = local ? "Local · SQLite y archivos" : "SQL Server · MongoDB · Elasticsearch · Redis" });
});
api.MapGet("/semantic/status", (EmbeddingClient client) => new { client.Enabled, client.Model });
api.MapGet("/semantic/ask", (string question, SemanticSearch search, CancellationToken ct) => search.AskAsync(question, ct));
api.MapGet("/systems", async (TechDocsDb db, CancellationToken ct) => await db.Systems.Include(x => x.Product).Select(x => new { x.Id, x.Name, Product = x.Product.Name }).ToListAsync(ct));
api.MapGet("/documents", async (string? q, string? state, Guid? systemId, int? page, int? pageSize, DateTimeOffset? from, DateTimeOffset? to, ISearchIndex search, CancellationToken ct) =>
{
    if (state is not null && !Enum.TryParse<DocumentState>(state, out _)) throw new DomainException("Estado no válido.");
    return await search.SearchAsync(new(q ?? "", state, systemId, Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 12, 1, 100), from, to), ct);
});
api.MapGet("/documents/{id:guid}", (Guid id, int? version, DocumentService service, CancellationToken ct) => service.ReadAsync(id, version, ct));
api.MapPost("/documents", async (DocumentRequest r, IValidator<DocumentRequest> validator, ClaimsPrincipal user, DocumentService service, CancellationToken ct) =>
{
    await validator.ValidateAndThrowAsync(r, ct);
    var id = await service.CreateAsync(r, Actor(user), ct);
    return Results.Created($"/api/documents/{id}", new { id });
}).RequireAuthorization(p => p.RequireRole("Redactor"));
api.MapPut("/documents/{id:guid}", async (Guid id, DocumentRequest r, IValidator<DocumentRequest> validator, ClaimsPrincipal user, DocumentService service, CancellationToken ct) =>
{
    await validator.ValidateAndThrowAsync(r, ct);
    return Results.Ok(new { version = await service.ReviseAsync(id, r, Actor(user), ct) });
}).RequireAuthorization(p => p.RequireRole("Redactor"));
api.MapPost("/documents/{id:guid}/transition", async (Guid id, TransitionRequest r, ClaimsPrincipal user, DocumentService service, CancellationToken ct) =>
{
    await service.TransitionAsync(id, r, Enum.Parse<Role>(user.FindFirstValue(ClaimTypes.Role)!), Actor(user), ct); return Results.NoContent();
});
api.MapGet("/documents/{id:guid}/diff", async (Guid id, int from, int to, TechDocsDb db, DocumentService service, CancellationToken ct) =>
{
    var versions = await db.Versions.Where(x => x.DocumentId == id && (x.Number == from || x.Number == to)).ToListAsync(ct);
    var a = versions.SingleOrDefault(x => x.Number == from) ?? throw new DomainException("Versión inicial no encontrada.", 404);
    var b = versions.SingleOrDefault(x => x.Number == to) ?? throw new DomainException("Versión final no encontrada.", 404);
    return new { from = await service.ReadContentAsync(a, ct), to = await service.ReadContentAsync(b, ct) };
});
api.MapGet("/parts", async (string? q, int? page, TechDocsDb db, CancellationToken ct) =>
{
    var query = db.Parts.Include(x => x.System).Include(x => x.Supplier).Where(x => q == null || x.Name.Contains(q) || x.Code.Contains(q));
    return new { total = await query.CountAsync(ct), items = await query.OrderBy(x => x.Code).Skip((Math.Max(1, page ?? 1) - 1) * 20).Take(20).Select(x => new { x.Id, x.Code, x.Name, System = x.System.Name, Supplier = x.Supplier.Name, x.Specification, x.Revision }).ToListAsync(ct) };
});
api.MapGet("/parts/{id:guid}", async (Guid id, TechDocsDb db, IServiceProvider services, CancellationToken ct) =>
{
    var cache = services.GetService<IConnectionMultiplexer>()?.GetDatabase(); var key = "techdocs:part:" + id;
    if (cache is not null && await cache.StringGetAsync(key) is { HasValue: true } cached) return Results.Content(cached.ToString(), "application/json");
    var part = await db.Parts.Include(x => x.System).Include(x => x.Supplier).Where(x => x.Id == id).Select(x => new { x.Id, x.Code, x.Name, System = x.System.Name, x.Specification, x.Revision, Supplier = x.Supplier.Name, Documents = db.DocumentParts.Where(d => d.PartId == id).Select(d => new { d.DocumentId, d.Document.Code, d.Document.Title }).ToList() }).SingleOrDefaultAsync(ct);
    if (part is null) return Results.NotFound();
    var json = JsonSerializer.Serialize(part, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if (cache is not null) await cache.StringSetAsync(key, json, TimeSpan.FromMinutes(5));
    return Results.Content(json, "application/json");
});
api.MapPut("/parts/{id:guid}", async (Guid id, PartRequest r, ClaimsPrincipal user, DocumentService service, IServiceProvider services, CancellationToken ct) =>
{
    var result = await service.UpdatePartAsync(id, r, Actor(user), ct);
    var redis = services.GetService<IConnectionMultiplexer>();
    if (redis is not null) await redis.GetDatabase().KeyDeleteAsync("techdocs:part:" + id);
    return Results.Ok(result);
}).RequireAuthorization(p => p.RequireRole("Redactor"));
api.MapPost("/imports", async (HttpRequest request, TechDocsDb db, IBackgroundJobClient jobs, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!request.HasFormContentType) throw new DomainException("Se requiere un archivo multipart/form-data.");
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault() ?? throw new DomainException("Selecciona un archivo.");
    if (file.Length > 10_000_000) throw new DomainException("El archivo supera 10 MB.", 413);
    using var stream = new MemoryStream(); await file.CopyToAsync(stream, ct); stream.Position = 0;
    List<ImportRow> rows;
    try { rows = ImportService.Parse(stream, Path.GetExtension(file.FileName).ToLowerInvariant()); }
    catch (Exception e) when (e is not DomainException && e is not OperationCanceledException) { throw new DomainException("No se ha podido leer el archivo. Comprueba su formato."); }
    var batch = new ImportBatch { FileName = Path.GetFileName(file.FileName), Payload = JsonSerializer.Serialize(rows), ActorId = Actor(user) };
    db.Imports.Add(batch); await db.SaveChangesAsync(ct);
    jobs.Enqueue<ImportService>(s => s.ProcessAsync(batch.Id, CancellationToken.None));
    return Results.Accepted($"/api/imports/{batch.Id}", new { batch.Id });
}).RequireAuthorization(p => p.RequireRole("Redactor")).DisableAntiforgery();
api.MapGet("/imports", async (TechDocsDb db, CancellationToken ct) => await db.Imports.OrderByDescending(x => x.CreatedAt).Take(30).Select(x => new { x.Id, x.FileName, x.Status, x.Accepted, x.Rejected, x.CreatedAt, x.FinishedAt, issues = x.ReportJson }).ToListAsync(ct));
api.MapGet("/tasks", async (TechDocsDb db, CancellationToken ct) => await db.ReviewTasks.OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.DocumentId, x.Reason, x.Completed, x.CreatedAt, document = db.Documents.Where(d => d.Id == x.DocumentId).Select(d => d.Title).First() }).ToListAsync(ct));
api.MapPost("/tasks/{id:guid}/complete", async (Guid id, ClaimsPrincipal user, TechDocsDb db, DocumentService service, CancellationToken ct) =>
{
    var task = await db.ReviewTasks.FindAsync([id], ct) ?? throw new DomainException("Tarea no encontrada.", 404);
    task.Completed = true; service.Audit(Actor(user), "task.completed", id, task.Reason); await db.SaveChangesAsync(ct); return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole("Revisor", "Aprobador"));
api.MapGet("/audit", async (int? page, TechDocsDb db, CancellationToken ct) => new { total = await db.Audit.CountAsync(ct), items = await db.Audit.OrderByDescending(x => x.Id).Skip((Math.Max(1, page ?? 1) - 1) * 30).Take(30).ToListAsync(ct) });
api.MapGet("/manual.pdf", async (Guid? systemId, string? ids, ManualExporter exporter, CancellationToken ct) => Results.File(await exporter.ExportAsync(systemId, ct, ids?.Split(',').Select(x => Guid.TryParse(x, out var id) ? id : throw new DomainException("Identificador inválido.")).ToArray()), "application/pdf", "TechDocs-Manual.pdf"));
api.MapPost("/sync/retry", async (TechDocsDb db, DocumentService service, CancellationToken ct) =>
{
    await db.Outbox.Where(x => x.ProcessedAt == null).ExecuteUpdateAsync(x => x.SetProperty(m => m.Attempts, 0), ct); await service.FlushOutboxAsync(ct); return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole("Aprobador"));
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: 404, title: "Recurso no encontrado."));
app.MapFallbackToFile("index.html");
await Setup.SeedAsync(app.Services);
using (var scope = app.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<IRecurringJobManager>().AddOrUpdate<DocumentService>("outbox", x => x.FlushOutboxAsync(CancellationToken.None), "* * * * *");
app.Run();
static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
public partial class Program;
