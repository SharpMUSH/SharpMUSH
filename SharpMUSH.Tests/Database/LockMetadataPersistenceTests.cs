using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Database;

public class LockMetadataPersistenceTests
{
	[Test]
	public async Task ProviderWritesAndReloadsExpressionFlagsAndCreator()
	{
		await using var world = await DefinitionCreationContract.Open();
		var target = (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		var data = new SharpLockData("=#1", LockService.LockFlags.Visual | LockService.LockFlags.Locked, target.Object().DBRef);
		await world.Database.SetLockAsync(target.Object(), "Basic", data);
		var loaded = (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		await Assert.That(loaded.Object().Locks["Basic"]).IsEqualTo(data);
		await world.Database.SetLockAsync(loaded.Object(), "Basic", data with { Creator = null });
		var legacy = (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		await Assert.That(legacy.Object().Locks["Basic"].Creator).IsNull();
		await Assert.That(legacy.Object().Locks["Basic"].Flags).IsEqualTo(data.Flags);
	}
}
