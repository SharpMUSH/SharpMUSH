using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
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
		var account = await factory.Services.GetRequiredService<IAccountService>().CreateAccountAsync(
			TestIsolationHelpers.GenerateUniqueName("SessionPersistence"), null, "Integration-Test-Pw-1!");
		await Assert.That(account.IsT0).IsTrue();
		var accountId = account.AsT0.Id!;
		var token = await store.CreateTokenAsync(accountId, TimeSpan.FromMinutes(15), "203.0.113.50");

		try
		{
			var fresh = new DatabaseAccountSessionStore(db); // simulates a server restart
			await Assert.That((await fresh.ValidateAsync(token))?.AccountId).IsEqualTo(accountId);

			await fresh.RevokeAllForIpAsync("203.0.113.50");
			await Assert.That(await fresh.ValidateAsync(token)).IsNull();
		}
		finally
		{
			await store.RevokeAsync(token);
		}
	}
}
