using Microsoft.EntityFrameworkCore;
using TechDocs.Domain;

namespace TechDocs.Infrastructure;

public sealed class TechDocsDb(DbContextOptions<TechDocsDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TechnicalSystem> Systems => Set<TechnicalSystem>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<TechnicalDocument> Documents => Set<TechnicalDocument>();
    public DbSet<DocumentVersion> Versions => Set<DocumentVersion>();
    public DbSet<DocumentPart> DocumentParts => Set<DocumentPart>();
    public DbSet<ReviewTask> ReviewTasks => Set<ReviewTask>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<ImportBatch> Imports => Set<ImportBatch>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Part>().HasIndex(x => x.Code).IsUnique();
        b.Entity<TechnicalDocument>().HasIndex(x => x.Code).IsUnique();
        b.Entity<DocumentVersion>().HasIndex(x => new { x.DocumentId, x.Number }).IsUnique();
        b.Entity<DocumentPart>().HasKey(x => new { x.DocumentId, x.PartId });
        b.Entity<Part>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<DocumentVersion>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<TechnicalDocument>().Property(x => x.CurrentVersion).IsConcurrencyToken();
        b.Entity<ReviewTask>().HasIndex(x => new { x.DocumentId, x.PartId, x.PartRevision }).IsUnique();
        b.Entity<Part>().HasOne(x => x.System).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<TechnicalDocument>().HasOne(x => x.System).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentPart>().HasOne(x => x.Part).WithMany().OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string)))
            {
                if (property.Name is "Code") property.SetMaxLength(60);
                if (property.Name is "Email") property.SetMaxLength(254);
            }
        if (Database.IsSqlite())
        {
            foreach (var entity in b.Model.GetEntityTypes())
                foreach (var p in entity.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset)))
                    p.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
        }
    }
}
