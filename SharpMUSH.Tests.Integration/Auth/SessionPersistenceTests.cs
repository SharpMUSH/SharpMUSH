using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// Proves that <see cref="DatabaseAccountSessionStore"/> persists sessions in the database
/// (survives a "server restart" — a fresh store instance backed by the same
/// <see cref="ISharpDatabase"/>) and that revocation by account/IP is immediate.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SessionPersistenceTests(ServerWebAppFactory factory)
{
	[Test]
	public async Task Session_SurvivesNewStoreInstance_AndRevokes()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		var store = new DatabaseAccountSessionStore(db);
		var token = await store.CreateTokenAsync("node_accounts/1", TimeSpan.FromMinutes(15), "203.0.113.50");

		var fresh = new DatabaseAccountSessionStore(db); // simulates a server restart
		await Assert.That(await fresh.ValidateAsync(token)).IsEqualTo("node_accounts/1");

		await fresh.RevokeAllForIpAsync("203.0.113.50");
		await Assert.That(await fresh.ValidateAsync(token)).IsNull();
	}
}
