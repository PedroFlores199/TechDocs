using FluentValidation;
using TechDocs.Domain;

namespace TechDocs.Application;

public sealed record LoginRequest(string Email, string Password);
public sealed record DocumentRequest(string Code, string Title, Guid SystemId, List<Block> Blocks, List<Guid> PartIds, int? ExpectedRevision = null);
public sealed record PartRequest(string Name, string Specification, int ExpectedRevision);
public sealed record TransitionRequest(string Action, int ExpectedRevision, string? Reason);
public sealed record SearchRequest(string Query = "", string? State = null, Guid? SystemId = null, int Page = 1, int PageSize = 12, DateTimeOffset? From = null, DateTimeOffset? To = null);
public sealed record ImportRow(int Row, string Code, string Name, string System, string Supplier, string Specification, string? Procedure);
public sealed record ImportIssue(int Row, string Code, string Error);
public sealed record SearchHit(Guid Id, string Code, string Title, string System, int Version, string State, string Excerpt, DateTimeOffset UpdatedAt, Guid SystemId = default);
public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed class DocumentValidator : AbstractValidator<DocumentRequest>
{
    public DocumentValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40).Matches("^[A-Z0-9-]+$");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(180);
        RuleFor(x => x.SystemId).NotEmpty();
        RuleFor(x => x.Blocks).NotEmpty().Must(x => x.Count <= 150);
        RuleForEach(x => x.Blocks).ChildRules(b =>
        {
            b.RuleFor(x => x.Type).Must(t => new[] { "step", "warning", "note", "heading", "table", "image" }.Contains(t));
            b.RuleFor(x => x.Text).NotEmpty().MaximumLength(10000);
            b.RuleFor(x => x).Must(x => x.Type != "image" || Uri.TryCreate(x.Text, UriKind.Absolute, out var u) && u.Scheme == "https");
        });
        RuleFor(x => x.PartIds).Must(x => x.Count <= 500 && x.Distinct().Count() == x.Count);
    }
}
public sealed class DomainException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
public static class ApprovalWorkflow
{
    public static void Apply(DocumentVersion version, string action, Role role, Guid actor, string? reason)
    {
        var allowed = action switch
        {
            "submit" => role == Role.Redactor && version.State == DocumentState.Draft,
            "review" => role == Role.Revisor && version.State == DocumentState.InReview && version.AuthorId != actor && version.ReviewedBy is null,
            "approve" => role == Role.Aprobador && version.State == DocumentState.InReview && version.ReviewedBy is not null && version.AuthorId != actor && version.ReviewedBy != actor,
            "reject" => role == Role.Revisor && version.State == DocumentState.InReview && !string.IsNullOrWhiteSpace(reason),
            "obsolete" => role == Role.Aprobador && version.State == DocumentState.Approved,
            _ => false
        };
        if (!allowed) throw new DomainException("La acción no corresponde al rol, al estado o a la separación de funciones.", 409);
        switch (action)
        {
            case "submit": version.State = DocumentState.InReview; break;
            case "review": version.ReviewedBy = actor; break;
            case "approve": version.State = DocumentState.Approved; break;
            case "reject": version.State = DocumentState.Draft; version.ReviewedBy = null; break;
            case "obsolete": version.State = DocumentState.Obsolete; break;
        }
        version.UpdatedAt = DateTimeOffset.UtcNow;
        version.Revision++;
    }
}
public interface IContentStore
{
    Task SaveAsync(VersionContent content, CancellationToken ct);
    Task<VersionContent?> ReadAsync(Guid versionId, CancellationToken ct);
}
public interface ISearchIndex
{
    Task IndexAsync(SearchHit hit, string text, CancellationToken ct);
    Task<PageResult<SearchHit>> SearchAsync(SearchRequest request, CancellationToken ct);
}
