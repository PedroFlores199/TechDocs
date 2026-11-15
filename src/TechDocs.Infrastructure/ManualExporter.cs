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
                    col.Item().PaddingTop(100).Text("TechDocs").FontSize(16).SemiBold().FontColor("17541F");
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
                    foreach (var (d, v, body) in entries) col.Item().PaddingBottom(15).Row(row =>
                    {
                        row.RelativeItem().SectionLink(d.Id.ToString()).Text($"{d.Code} · {body.Title ?? d.Title} · v{v.Number}");
                        row.ConstantItem(35).AlignRight().Text(t => t.BeginPageNumberOfSection(d.Id.ToString()));
                    });
                });
            });
            foreach (var (d, v, body) in entries) container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(48); page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Column(c => { c.Item().Text($"{d.Code} / v{v.Number} / Aprobado").FontSize(10).FontColor("17541F"); c.Item().PaddingTop(10).Text(body.Title ?? d.Title).FontSize(22).SemiBold(); });
                page.Content().Section(d.Id.ToString()).PaddingTop(25).Column(col =>
                {
                    var step = 0;
                    foreach (var b in body.Blocks)
                    {
                        var item = col.Item().PaddingBottom(14);
                        if (b.Type == "heading") item.PaddingTop(4).Text(b.Text).FontSize(14).SemiBold();
                        else if (b.Type == "warning") item.Background("FFF4DE").Padding(10).Text("Advertencia · " + b.Text);
                        else if (b.Type == "table")
                        {
                            var rows = b.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('|')).ToArray();
                            var count = rows.Length == 0 ? 1 : rows.Max(x => x.Length);
                            item.Table(table =>
                            {
                                table.ColumnsDefinition(columns => { for (var i = 0; i < count; i++) columns.RelativeColumn(); });
                                for (var row = 0; row < rows.Length; row++)
                                    for (var column = 0; column < count; column++)
                                    {
                                        var cell = table.Cell().BorderBottom(0.5f).BorderColor("DDE5DF").Background(row == 0 ? "F1F5F2" : "FFFFFF").Padding(7);
                                        var text = cell.Text(column < rows[row].Length ? rows[row][column].Trim() : "");
                                        if (row == 0) text.SemiBold();
                                    }
                            });
                        }
                        else item.Padding(6).Text(b.Type == "step" ? $"{++step}. {b.Text}" : b.Text);
                    }
                    col.Item().PaddingTop(25).Text("SHA-256: " + v.Hash).FontSize(7);
                });
                page.Footer().Row(r => { r.RelativeItem().Text("TechDocs · Atlas R-240").FontSize(8); r.ConstantItem(60).AlignRight().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); }); });
            });
        }).GeneratePdf();
    }
}
