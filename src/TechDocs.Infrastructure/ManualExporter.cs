using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TechDocs.Application;
using TechDocs.Domain;

namespace TechDocs.Infrastructure;

public sealed class ManualExporter(TechDocsDb db, DocumentService documents)
{
    public async Task<byte[]> ExportAsync(Guid? systemId, CancellationToken ct, Guid[]? ids = null)
    {
        var docs = await db.Documents.Include(x => x.Versions).Include(x => x.System).Where(x => (systemId == null || x.SystemId == systemId) && (ids == null || ids.Contains(x.Id))).OrderBy(x => x.Code).ToListAsync(ct);
        var entries = new List<(TechnicalDocument Doc, DocumentVersion Version, VersionContent Body)>();
        foreach (var d in docs)
        {
            if (d.Versions.Single(x => x.Number == d.CurrentVersion).State == DocumentState.Obsolete) continue;
            var version = d.Versions.Where(x => x.State == DocumentState.Approved).OrderByDescending(x => x.Number).FirstOrDefault();
            if (version is not null) entries.Add((d, version, await documents.ReadContentAsync(version, ct)));
        }
        if (entries.Count == 0) throw new DomainException("No hay versiones aprobadas para exportar.", 409);
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(48); page.DefaultTextStyle(x => x.FontSize(10).FontColor("263648"));
                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(100).Text("TECHDOCS").FontSize(13).LetterSpacing(3).FontColor("936D37");
                    col.Item().PaddingTop(30).Text("Manual de mantenimiento").FontSize(36).SemiBold();
                    col.Item().PaddingTop(20).Text("Atlas R-240 · Documentación técnica").FontSize(16);
                    col.Item().PaddingTop(40).Text($"Edición {DateTimeOffset.UtcNow:yyyy-MM-dd} · {entries.Count} procedimientos aprobados");
                    col.Item().PaddingTop(18).Text("Catálogo y procedimientos ficticios. Documento de demostración de portfolio.").FontColor(Colors.Grey.Medium);
                });
            });
            container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(48);
                page.Header().Text("Índice de procedimientos").FontSize(22);
                page.Content().PaddingTop(30).Column(col =>
                {
                    foreach (var (d, v, _) in entries) col.Item().PaddingBottom(15).Row(row =>
                    {
                        row.RelativeItem().SectionLink(d.Id.ToString()).Text($"{d.Code} · {d.Title} · v{v.Number}");
                        row.ConstantItem(35).AlignRight().Text(t => t.BeginPageNumberOfSection(d.Id.ToString()));
                    });
                });
            });
            foreach (var (d, v, body) in entries) container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(48); page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Column(c => { c.Item().Text($"{d.Code} / v{v.Number} / APROBADO").FontSize(10).FontColor("936D37"); c.Item().PaddingTop(10).Text(d.Title).FontSize(22).SemiBold(); });
                page.Content().Section(d.Id.ToString()).PaddingTop(25).Column(col =>
                {
                    foreach (var b in body.Blocks) col.Item().PaddingBottom(14).Background(b.Type == "warning" ? "FFF4DE" : "FFFFFF").Padding(8).Text((b.Type == "warning" ? "ADVERTENCIA · " : "") + b.Text);
                    col.Item().PaddingTop(25).Text("SHA-256: " + v.Hash).FontSize(7);
                });
                page.Footer().Row(r => { r.RelativeItem().Text("TechDocs · Atlas R-240").FontSize(8); r.ConstantItem(60).AlignRight().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); }); });
            });
        }).GeneratePdf();
    }
}
