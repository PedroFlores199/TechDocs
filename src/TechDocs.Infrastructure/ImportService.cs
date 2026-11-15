using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TechDocs.Application;
using TechDocs.Domain;

namespace TechDocs.Infrastructure;

public sealed class ImportService(TechDocsDb db, DocumentService documents)
{
    public static List<ImportRow> Parse(Stream stream, string extension)
    {
        var rows = new List<ImportRow>();
        if (extension == ".xlsx")
        {
            using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, true);
            if (zip.Entries.Sum(x => x.Length) > 100_000_000) throw new DomainException("El libro supera 100 MB una vez descomprimido.");
            stream.Position = 0;
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();
            var headers = new[] { "Code", "Name", "System", "Supplier", "Specification", "Procedure" };
            if (!headers.Select((h, i) => sheet.Cell(1, i + 1).GetString().Trim() == h).All(x => x)) throw new DomainException("Las columnas deben ser Code, Name, System, Supplier, Specification y Procedure.");
            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                if (rows.Count >= 50_000) throw new DomainException("Máximo 50.000 filas por importación.");
                rows.Add(new(row.RowNumber(), row.Cell(1).GetString().Trim().ToUpperInvariant(), row.Cell(2).GetString().Trim(), row.Cell(3).GetString().Trim(), row.Cell(4).GetString().Trim(), row.Cell(5).GetString().Trim(), row.Cell(6).GetString().Trim()));
            }
        }
        else if (extension == ".xml")
        {
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 25_000_000 });
            var xml = XDocument.Load(reader);
            rows = xml.Root?.Elements("Part").Select((x, i) => new ImportRow(i + 2, V(x, "Code").ToUpperInvariant(), V(x, "Name"), V(x, "System"), V(x, "Supplier"), V(x, "Specification"), V(x, "Procedure"))).ToList() ?? [];
        }
        else throw new DomainException("Solo se admiten .xlsx y .xml.");
        if (rows.Count is 0 or > 50_000) throw new DomainException("El archivo debe contener entre 1 y 50.000 filas.");
        return rows;
    }
    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        var batch = await db.Imports.SingleAsync(x => x.Id == id, ct);
        if (batch.Status == "Completed") return;
        batch.Status = "Processing";
        await db.SaveChangesAsync(ct);
        var issues = new List<ImportIssue>();
        var rows = JsonSerializer.Deserialize<List<ImportRow>>(batch.Payload)!;
        var systems = await db.Systems.ToDictionaryAsync(x => x.Name, ct);
        var suppliers = (await db.Suppliers.ToListAsync(ct)).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var codes = (await db.Parts.Select(x => x.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accepted = new List<(Part Part, ImportRow Row)>();
        foreach (var r in rows)
        {
            string? error = null;
            if (string.IsNullOrWhiteSpace(r.Code) || r.Code.Length > 40 || !System.Text.RegularExpressions.Regex.IsMatch(r.Code, "^[A-Z0-9-]+$")) error = "Código vacío o inválido";
            else if (r.Name.Length is 0 or > 180 || r.Supplier.Length is 0 or > 180 || r.Specification.Length > 2000 || r.Procedure?.Length > 10000) error = "Campos vacíos o demasiado largos";
            else if (!systems.ContainsKey(r.System)) error = "Sistema desconocido";
            else if (!codes.Add(r.Code)) error = "Código duplicado";
            if (error is not null) { issues.Add(new(r.Row, r.Code, error)); continue; }
            if (!suppliers.TryGetValue(r.Supplier, out var supplier)) { supplier = new() { Name = r.Supplier }; suppliers.Add(r.Supplier, supplier); db.Suppliers.Add(supplier); }
            var part = new Part { Code = r.Code, Name = r.Name, SystemId = systems[r.System].Id, SupplierId = supplier.Id, Specification = r.Specification };
            accepted.Add((part, r));
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Parts.AddRange(accepted.Select(x => x.Part));
        foreach (var (part, row) in accepted.Where(x => !string.IsNullOrWhiteSpace(x.Row.Procedure)))
        {
            var doc = new TechnicalDocument { Code = "IMP-" + part.Code, Title = "Mantenimiento · " + part.Name, SystemId = part.SystemId };
            var version = new DocumentVersion { DocumentId = doc.Id, Number = 1, AuthorId = batch.ActorId };
            doc.Versions.Add(version); doc.Parts.Add(new() { DocumentId = doc.Id, PartId = part.Id });
            db.Documents.Add(doc);
            var body = new VersionContent(version.Id.ToString(), doc.Id, 1, [new("step", row.Procedure!, part.Code)], doc.Title, doc.SystemId, [part.Id]);
            var payload = JsonSerializer.Serialize(body);
            version.Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
            db.Outbox.Add(new() { VersionId = version.Id, Payload = payload });
        }
        batch.Accepted = accepted.Count; batch.Rejected = issues.Count; batch.ReportJson = JsonSerializer.Serialize(issues); batch.Payload = "[]"; batch.Status = "Completed"; batch.FinishedAt = DateTimeOffset.UtcNow;
        documents.Audit(batch.ActorId, "import.completed", id, $"{batch.Accepted} correctas; {batch.Rejected} rechazadas");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await documents.FlushOutboxAsync(ct);
    }
    private static string V(XElement e, string key) => e.Element(key)?.Value.Trim() ?? "";
}
