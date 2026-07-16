using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class SessionStoreDbTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static SharpSession Make(string token, string acct, string ip) => new()
	{
		Token = token, AccountId = acct, OriginIp = ip,
		ExpiryUnixMs = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeMilliseconds(),
		TtlMs = (long)TimeSpan.FromMinutes(15).TotalMilliseconds
	};

	[Test, NotInParallel(nameof(SessionStoreDbTests))]
	public async Task Upsert_Get_Delete_RoundTrip()
	{
		var s = Make("tok-rt-1", "node_accounts/1", "203.0.113.9");
		await Db.UpsertSessionAsync(s);
		var got = await Db.GetSessionAsync("tok-rt-1");
		await Assert.That(got).IsNotNull();
		await Assert.That(got!.AccountId).IsEqualTo("node_accounts/1");
		await Assert.That(got.OriginIp).IsEqualTo("203.0.113.9");

		await Db.DeleteSessionAsync("tok-rt-1");
		await Assert.That(await Db.GetSessionAsync("tok-rt-1")).IsNull();
	}

	[Test, NotInParallel(nameof(SessionStoreDbTests))]
	public async Task DeleteForAccount_And_ForIp()
	{
		await Db.UpsertSessionAsync(Make("tok-a1", "acctX", "10.0.0.1"));
		await Db.UpsertSessionAsync(Make("tok-a2", "acctX", "10.0.0.2"));
		await Db.UpsertSessionAsync(Make("tok-b1", "acctY", "10.0.0.1"));

		await Db.DeleteSessionsForAccountAsync("acctX");
		await Assert.That(await Db.GetSessionAsync("tok-a1")).IsNull();
		await Assert.That(await Db.GetSessionAsync("tok-a2")).IsNull();
		await Assert.That(await Db.GetSessionAsync("tok-b1")).IsNotNull();

		await Db.DeleteSessionsForIpAsync("10.0.0.1");
		await Assert.That(await Db.GetSessionAsync("tok-b1")).IsNull();
	}
}
