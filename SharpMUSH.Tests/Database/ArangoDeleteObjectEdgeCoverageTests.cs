using Core.Arango;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// <see cref="DatabaseConstants.edgeCollections"/> is the list ArangoDB object deletion sweeps for
/// edges incident to the object it is removing. It is hand-maintained, and an edge collection
/// missing from it fails silently: the object goes away, the edge does not, and whatever sat on the
/// other end keeps a live reference to a deleted row.
/// <para>
/// So this asks the live database which of its collections are edges, and requires the constant to
/// cover every one. Adding an edge collection without adding it here fails right here instead of
/// months later as a dangling reference.
/// </para>
/// </summary>
public class ArangoDeleteObjectEdgeCoverageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task EdgeCollectionsConstant_CoversEveryEdgeCollectionInTheDatabase()
	{
		// Arango only: the constant drives the ArangoDB provider's sweep. SurrealDB keeps its own
		// relation-table list next to its delete, and Memgraph's DETACH DELETE needs no list at all.
		if (WebAppFactoryArg.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = WebAppFactoryArg.Services.GetRequiredService<ArangoHandle>();
		var collections = await context.Collection.ListAsync(handle);

		var edgeCollectionsInDatabase = collections
			.Where(c => c.Type == Core.Arango.Protocol.ArangoCollectionType.Edge)
			.Select(c => c.Name)
			.Where(name => !name.StartsWith('_'))
			.Order(StringComparer.Ordinal)
			.ToArray();

		await Assert.That(edgeCollectionsInDatabase).IsNotEmpty()
			.Because("if the database reports no edge collections the check below proves nothing");

		var missing = edgeCollectionsInDatabase
			.Except(DatabaseConstants.edgeCollections, StringComparer.Ordinal)
			.ToArray();

		await Assert.That(missing).IsEmpty()
			.Because($"DatabaseConstants.edgeCollections must list every edge collection, or "
				+ $"DeleteObjectAsync leaves its edges behind. Missing: {string.Join(", ", missing)}");
	}
}
