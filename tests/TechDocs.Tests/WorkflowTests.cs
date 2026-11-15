using FluentAssertions;
using TechDocs.Application;
using TechDocs.Domain;
using TechDocs.Infrastructure;
using Xunit;

namespace TechDocs.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public void ApprovalRequiresIndependentReview()
    {
        var author = Guid.NewGuid(); var reviewer = Guid.NewGuid(); var approver = Guid.NewGuid();
        var v = new DocumentVersion { AuthorId = author, State = DocumentState.Draft };
        ApprovalWorkflow.Apply(v, "submit", Role.Redactor, author, null);
        var premature = () => ApprovalWorkflow.Apply(v, "approve", Role.Aprobador, approver, null);
        premature.Should().Throw<DomainException>();
        ApprovalWorkflow.Apply(v, "review", Role.Revisor, reviewer, null);
        ApprovalWorkflow.Apply(v, "approve", Role.Aprobador, approver, null);
        v.State.Should().Be(DocumentState.Approved); v.Revision.Should().Be(4);
    }
    [Theory]
    [InlineData(Role.Lector, "submit", DocumentState.Draft)]
    [InlineData(Role.Redactor, "approve", DocumentState.InReview)]
    [InlineData(Role.Aprobador, "review", DocumentState.InReview)]
    [InlineData(Role.Revisor, "obsolete", DocumentState.Approved)]
    [InlineData(Role.Redactor, "submit", DocumentState.Approved)]
    public void InvalidRolesAndTransitionsAreRejected(Role role, string action, DocumentState state)
    {
        var apply = () => ApprovalWorkflow.Apply(new() { AuthorId = Guid.NewGuid(), State = state }, action, role, Guid.NewGuid(), null);
        apply.Should().Throw<DomainException>();
    }
    [Fact]
    public void AuthorCannotReviewOwnVersion()
    {
        var id = Guid.NewGuid(); var apply = () => ApprovalWorkflow.Apply(new() { AuthorId = id, State = DocumentState.InReview }, "review", Role.Revisor, id, null);
        apply.Should().Throw<DomainException>();
    }
    [Fact]
    public void RejectRequiresReasonAndClearsReview()
    {
        var v = new DocumentVersion { State = DocumentState.InReview, AuthorId = Guid.NewGuid(), ReviewedBy = Guid.NewGuid() };
        var invalid = () => ApprovalWorkflow.Apply(v, "reject", Role.Revisor, Guid.NewGuid(), ""); invalid.Should().Throw<DomainException>();
        ApprovalWorkflow.Apply(v, "reject", Role.Revisor, Guid.NewGuid(), "Falta el par de apriete"); v.State.Should().Be(DocumentState.Draft); v.ReviewedBy.Should().BeNull();
    }
    [Fact]
    public void PasswordsAreSaltedAndVerified()
    {
        var a = Setup.HashPassword("Example123!"); var b = Setup.HashPassword("Example123!"); a.Should().NotBe(b); Setup.VerifyPassword("Example123!", a).Should().BeTrue(); Setup.VerifyPassword("wrong", a).Should().BeFalse();
    }
    [Fact]
    public void XmlCannotLoadExternalEntities()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE Parts [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><Parts><Part><Code>&x;</Code></Part></Parts>"));
        var parse = () => ImportService.Parse(stream, ".xml"); parse.Should().Throw<System.Xml.XmlException>();
    }
    [Fact]
    public void XmlNormalizesCodesAndTrimsFields()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<Parts><Part><Code> atl-9 </Code><Name> Junta </Name><System>Hidráulica</System></Part></Parts>"));
        var row = ImportService.Parse(stream, ".xml").Single(); row.Code.Should().Be("ATL-9"); row.Name.Should().Be("Junta");
    }
    [Fact]
    public void ValidatorRejectsEmptyBlocksAndUnsafeImageUrls()
    {
        var validator = new DocumentValidator();
        validator.Validate(new DocumentRequest("MP-X", "Test", Guid.NewGuid(), [], [])).IsValid.Should().BeFalse();
        validator.Validate(new DocumentRequest("MP-X", "Test", Guid.NewGuid(), [new("image", "javascript:alert(1)")], [])).IsValid.Should().BeFalse();
    }
}
