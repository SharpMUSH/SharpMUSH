using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Provider-level round-trip for <see cref="ISharpDatabase.SetPlayerQuotaAsync"/>. Before the
/// fix, <c>SetPlayerQuotaAsync</c> put the full <c>_id</c> ("node_players/&lt;key&gt;") into the
/// Arango update payload where a bare <c>_key</c> is required, throwing
/// <c>[1205] illegal document identifier</c> at runtime. This drives the real provider against
/// the real ArangoDB container: create a player, set its quota, read it back, and assert the new
/// value persisted — which reproduces the throw on the old code and verifies the write on the fix.
/// </summary>
public class QuotaDbTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration =>
		WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	[Test, NotInParallel(nameof(QuotaDbTests))]
	public async Task SetPlayerQuota_RoundTrips()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var name = TestIsolationHelpers.GenerateUniqueName("QuotaDb");

		await Db.CreatePlayerAsync(name, "TestPassword123", defaultHome, defaultHome, startingQuota);

		var player = await Db.GetPlayerByNameOrAliasAsync(name).FirstOrDefaultAsync();
		await Assert.That(player).IsNotNull();

		// The value under test must differ from the starting quota so a no-op write cannot pass.
		var newQuota = startingQuota + 22;
		await Db.SetPlayerQuotaAsync(player!, newQuota);

		var reloaded = await Db.GetPlayerByNameOrAliasAsync(name).FirstOrDefaultAsync();
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Quota).IsEqualTo(newQuota);
	}
}
