using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechDocs.Application;
using TechDocs.Domain;

namespace TechDocs.Infrastructure;

public sealed class DocumentService(TechDocsDb db, IContentStore content, ISearchIndex search)
{
    public async Task<Guid> CreateAsync(DocumentRequest request, Guid actor, CancellationToken ct)
    {
        await ValidateReferences(request, ct);
        if (await db.Documents.AnyAsync(x => x.Code == request.Code, ct)) throw new DomainException("El código de documento ya existe.", 409);
        var d = new TechnicalDocument { Code = request.Code, Title = request.Title.Trim(), SystemId = request.SystemId };
        var v = new DocumentVersion { DocumentId = d.Id, Number = 1, AuthorId = actor };
        d.Versions.Add(v);
        d.Parts = request.PartIds.Select(id => new DocumentPart { DocumentId = d.Id, PartId = id }).ToList();
        db.Documents.Add(d);
        QueueContent(v, request.Blocks, request.Title, request.SystemId, request.PartIds);
        Audit(actor, "document.created", d.Id, request.Title);
        await db.SaveChangesAsync(ct);
        await FlushOutboxAsync(ct);
        return d.Id;
    }
    public async Task<int> ReviseAsync(Guid id, DocumentRequest request, Guid actor, CancellationToken ct)
    {
        await ValidateReferences(request, ct);
        var d = await db.Documents.Include(x => x.Versions).Include(x => x.Parts).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Documento no encontrado.", 404);
        var previous = d.Versions.Single(x => x.Number == d.CurrentVersion);
        if (request.ExpectedRevision != previous.Revision) throw new DomainException("El documento ha cambiado. Recarga antes de guardar.", 409);
        if (previous.State == DocumentState.InReview) throw new DomainException("Devuelve la versión a borrador antes de editar.", 409);
        if (d.Code != request.Code) throw new DomainException("El código identifica el documento y no puede cambiar.");
        d.Title = request.Title.Trim();
        d.SystemId = request.SystemId;
        d.CurrentVersion++;
        var v = new DocumentVersion { DocumentId = id, Number = d.CurrentVersion, AuthorId = actor };
        d.Versions.Add(v);
        db.Versions.Add(v);
        var removed = d.Parts.Where(x => !request.PartIds.Contains(x.PartId)).ToArray();
        db.DocumentParts.RemoveRange(removed);
        foreach (var p in request.PartIds.Where(p => d.Parts.All(x => x.PartId != p))) d.Parts.Add(new() { DocumentId = id, PartId = p });
        QueueContent(v, request.Blocks, request.Title, request.SystemId, request.PartIds);
        Audit(actor, "document.revised", id, $"Versión {v.Number}");
        await db.SaveChangesAsync(ct);
        await FlushOutboxAsync(ct);
        return v.Number;
    }
    public async Task TransitionAsync(Guid id, TransitionRequest request, Role role, Guid actor, CancellationToken ct)
    {
        var d = await db.Documents.Include(x => x.Versions).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Documento no encontrado.", 404);
        var v = d.Versions.Single(x => x.Number == d.CurrentVersion);
        if (v.Revision != request.ExpectedRevision) throw new DomainException("La versión ha cambiado. Recarga antes de continuar.", 409);
        ApprovalWorkflow.Apply(v, request.Action, role, actor, request.Reason);
        Audit(actor, "document." + request.Action, id, request.Reason ?? v.State.ToString());
        var body = await ReadContentAsync(v, ct);
        QueueContent(v, body.Blocks, body.Title, body.SystemId, body.PartIds);
        await db.SaveChangesAsync(ct);
        await FlushOutboxAsync(ct);
    }
    public async Task<object> ReadAsync(Guid id, int? number, CancellationToken ct)
    {
        var d = await db.Documents.Include(x => x.System).Include(x => x.Versions).Include(x => x.Parts).ThenInclude(x => x.Part).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Documento no encontrado.", 404);
        var v = d.Versions.SingleOrDefault(x => x.Number == (number ?? d.CurrentVersion)) ?? throw new DomainException("Versión no encontrada.", 404);
        var body = await ReadContentAsync(v, ct);
        var historicalParts = body.PartIds is null ? d.Parts.Select(x => x.Part).ToArray() : await db.Parts.AsNoTracking().Where(x => body.PartIds.Contains(x.Id)).ToArrayAsync(ct);
        var historicalSystem = body.SystemId is null ? d.System.Name : await db.Systems.Where(x => x.Id == body.SystemId).Select(x => x.Name).SingleAsync(ct);
        return new { d.Id, d.Code, Title = body.Title ?? d.Title, SystemId = body.SystemId ?? d.SystemId, System = historicalSystem, d.CurrentVersion, Version = v.Number, State = v.State.ToString(), v.Revision, v.ReviewedBy, v.AuthorId, v.CreatedAt, v.UpdatedAt, body.Blocks, Parts = historicalParts.Select(x => new { x.Id, x.Code, x.Name }), Versions = d.Versions.OrderByDescending(x => x.Number).Select(x => new { x.Number, State = x.State.ToString(), x.CreatedAt, x.Hash }) };
    }
    public async Task<VersionContent> ReadContentAsync(DocumentVersion version, CancellationToken ct)
    {
        var body = await content.ReadAsync(version.Id, ct);
        if (body is not null) return body;
        var pending = await db.Outbox.Where(x => x.VersionId == version.Id).OrderByDescending(x => x.Id).Select(x => x.Payload).FirstOrDefaultAsync(ct);
        return pending is null ? throw new DomainException("El contenido aún no está disponible.", 503) : JsonSerializer.Deserialize<VersionContent>(pending)!;
    }
    public async Task<object> UpdatePartAsync(Guid id, PartRequest request, Guid actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 180 || request.Specification.Length > 2000) throw new DomainException("Nombre o especificación no válidos.");
        var p = await db.Parts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Pieza no encontrada.", 404);
        if (p.Revision != request.ExpectedRevision) throw new DomainException("La pieza ha cambiado. Recarga antes de guardar.", 409);
        var old = new { p.Name, p.Specification, p.Revision };
        p.Name = request.Name.Trim(); p.Specification = request.Specification.Trim(); p.Revision++; p.UpdatedAt = DateTimeOffset.UtcNow;
        var affected = await db.DocumentParts.Where(x => x.PartId == id).Select(x => new { x.DocumentId, x.Document.Title, x.Document.Code }).ToListAsync(ct);
        foreach (var d in affected) db.ReviewTasks.Add(new() { DocumentId = d.DocumentId, PartId = id, PartRevision = p.Revision, Reason = $"{p.Code}: revisión {old.Revision} → {p.Revision}" });
        Audit(actor, "part.updated", id, JsonSerializer.Serialize(new { Before = old, After = new { p.Name, p.Specification, p.Revision } }));
        await db.SaveChangesAsync(ct);
        return new { Part = new { p.Id, p.Name, p.Revision }, AffectedDocuments = affected };
    }
    public async Task FlushOutboxAsync(CancellationToken ct)
    {
        var messages = await db.Outbox.Where(x => x.ProcessedAt == null && x.Attempts < 20).OrderBy(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var m in messages)
        {
            try
            {
                var body = JsonSerializer.Deserialize<VersionContent>(m.Payload)!;
                await content.SaveAsync(body, ct);
                var d = await db.Documents.Include(x => x.Versions).Include(x => x.System).SingleAsync(x => x.Id == body.DocumentId, ct);
                var v = d.Versions.Single(x => x.Number == body.Number);
                var text = string.Join(' ', body.Blocks.Select(x => x.Text));
                if (d.CurrentVersion == body.Number) await search.IndexAsync(new(d.Id, d.Code, d.Title, d.System.Name, v.Number, v.State.ToString(), text[..Math.Min(text.Length, 240)], v.UpdatedAt, d.SystemId), text, ct);
                m.ProcessedAt = DateTimeOffset.UtcNow; m.LastError = null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                m.Attempts++; m.LastError = e.GetType().Name + ": " + e.Message[..Math.Min(e.Message.Length, 500)];
            }
        }
        await db.SaveChangesAsync(ct);
    }
    private void QueueContent(DocumentVersion version, List<Block> blocks, string? title = null, Guid? systemId = null, List<Guid>? partIds = null)
    {
        var json = JsonSerializer.Serialize(new VersionContent(version.Id.ToString(), version.DocumentId, version.Number, blocks, title, systemId, partIds));
        version.Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        db.Outbox.Add(new() { VersionId = version.Id, Payload = json });
    }
    private async Task ValidateReferences(DocumentRequest r, CancellationToken ct)
    {
        if (!await db.Systems.AnyAsync(x => x.Id == r.SystemId, ct)) throw new DomainException("El sistema no existe.");
        if (await db.Parts.CountAsync(x => r.PartIds.Contains(x.Id), ct) != r.PartIds.Count) throw new DomainException("Alguna pieza no existe.");
    }
    public void Audit(Guid actor, string action, Guid entity, string detail) => db.Audit.Add(new() { Actor = actor.ToString(), Action = action, Entity = entity.ToString(), Detail = detail });
}
