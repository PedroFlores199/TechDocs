using FluentAssertions;
using TechDocs.Domain;
using TechDocs.Infrastructure;
using Testcontainers.MongoDb;

namespace TechDocs.Tests;

public sealed class MongoTests
{
    [ContainerFact]
    public async Task VersionSnapshotsPreserveNullableGuidsAndPartReferences()
    {
        await using var server = new MongoDbBuilder("mongo:8.0").Build();
        await server.StartAsync();
        var store = new MongoContentStore(server.GetConnectionString());
        var versionId = Guid.NewGuid();
        var systemId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        var original = new VersionContent(versionId.ToString(), Guid.NewGuid(), 1, [new("step", "Inspeccionar junta")], "Procedimiento", systemId, [partId]);
        await store.SaveAsync(original, default);
        await store.SaveAsync(original, default);
        var result = await store.ReadAsync(versionId, default);
        result.Should().NotBeNull();
        result!.SystemId.Should().Be(systemId);
        result.PartIds.Should().Equal(partId);
        result.Title.Should().Be("Procedimiento");
        result.Blocks.Should().ContainSingle();
    }
}
