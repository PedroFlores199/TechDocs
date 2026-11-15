namespace TechDocs.Domain;

public enum DocumentState { Draft, InReview, Approved, Obsolete }
public enum Role { Redactor, Revisor, Aprobador, Lector }
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
}
public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
}
public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
}
public sealed class TechnicalSystem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
}
public sealed class Part
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid SystemId { get; set; }
    public TechnicalSystem System { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string Specification { get; set; } = "";
    public int Revision { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class TechnicalDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid SystemId { get; set; }
    public TechnicalSystem System { get; set; } = null!;
    public int CurrentVersion { get; set; } = 1;
    public List<DocumentVersion> Versions { get; set; } = [];
    public List<DocumentPart> Parts { get; set; } = [];
}
public sealed class DocumentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public TechnicalDocument Document { get; set; } = null!;
    public int Number { get; set; }
    public DocumentState State { get; set; }
    public Guid AuthorId { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Hash { get; set; } = "";
    public int Revision { get; set; } = 1;
}
public sealed class DocumentPart
{
    public Guid DocumentId { get; set; }
    public TechnicalDocument Document { get; set; } = null!;
    public Guid PartId { get; set; }
    public Part Part { get; set; } = null!;
}
public sealed class ReviewTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid PartId { get; set; }
    public int PartRevision { get; set; }
    public string Reason { get; set; } = "";
    public bool Completed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class AuditEntry
{
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public string ReportJson { get; set; } = "[]";
    public string Payload { get; set; } = "";
    public Guid ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
}
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public Guid VersionId { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
public sealed record Block(string Type, string Text, string? PartCode = null);
public sealed record VersionContent(string Id, Guid DocumentId, int Number, List<Block> Blocks, string? Title = null, Guid? SystemId = null, List<Guid>? PartIds = null);
