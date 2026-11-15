using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TechDocs.Domain;
using TechDocs.Infrastructure;
using Testcontainers.MsSql;
using Xunit;

namespace TechDocs.Tests;

public sealed class ContainerFactAttribute : FactAttribute
{
    public ContainerFactAttribute() { if (Environment.GetEnvironmentVariable("RUN_CONTAINER_TESTS") != "1") Skip = "Activa RUN_CONTAINER_TESTS=1 para probar SQL Server en Docker."; }
}
public sealed class SqlServerTests
{
    [ContainerFact]
    public async Task RelationalConstraintsAndTransactionsUseRealSqlServer()
    {
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").WithCreateParameterModifier(p => p.Platform = "linux/amd64").Build(); await server.StartAsync();
        await using var db = new TechDocsDb(new DbContextOptionsBuilder<TechDocsDb>().UseSqlServer(server.GetConnectionString()).Options); await db.Database.EnsureCreatedAsync();
        var p = new Product { Name = "Prueba", Code = "TEST" }; var s = new TechnicalSystem { ProductId = p.Id, Name = "Hidráulica" }; var supplier = new Supplier { Name = "Test" }; db.AddRange(p, s, supplier); await db.SaveChangesAsync();
        var part = new Part { Code = "TEST-1", Name = "Junta", SystemId = s.Id, SupplierId = supplier.Id }; db.Parts.Add(part); await db.SaveChangesAsync();
        await using (var tx = await db.Database.BeginTransactionAsync()) { db.Parts.Add(new() { Code = "ROLLBACK", Name = "No persistir", SystemId = s.Id, SupplierId = supplier.Id }); await db.SaveChangesAsync(); await tx.RollbackAsync(); }
        db.ChangeTracker.Clear(); (await db.Parts.CountAsync()).Should().Be(1);
        db.Parts.Add(new() { Code = "TEST-1", Name = "Duplicado", SystemId = s.Id, SupplierId = supplier.Id }); var save = () => db.SaveChangesAsync(); await save.Should().ThrowAsync<DbUpdateException>();
    }
}
