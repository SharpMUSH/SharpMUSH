using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

public class SurrealExpandedKeyTests
{
	[Test]
	public async Task ServerDataKeysRemainIndependentUnderThePinnedEmbeddedProvider()
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=keyisolation;Database=test{Guid.NewGuid():N}").AddInMemoryProvider();
		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		await database.SetExpandedServerData("reality.config.v1", "first");
		await database.SetExpandedServerData("roles.migration.v1", "second");
		await Assert.That(await database.GetExpandedServerData<string>("reality.config.v1")).IsEqualTo("first");
		await Assert.That(await database.GetExpandedServerData<string>("roles.migration.v1")).IsEqualTo("second");
		await Assert.That(await database.GetExpandedServerData<string>("missing")).IsNull();
	}
}
