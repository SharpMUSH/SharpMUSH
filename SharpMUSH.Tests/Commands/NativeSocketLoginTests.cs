using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NativeSocketLoginTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test, NotInParallel(["ConfigMutation"])]
	public async Task NativeSecretBindsNormallyOnceAndPortalLogoutLeavesEstablishedNativeConnections()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var accounts = services.GetRequiredService<IAccountService>();
		var sessions = services.GetRequiredService<IAccountSessionStore>();
		var connections = services.GetRequiredService<IConnectionService>();
		var native = services.GetRequiredService<NativeLoginService>();
		var proofs = services.GetRequiredService<IRecentAccountProofStore>();
		var character = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "nativejourney");
		var account = (await accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("nativeowner"), null, "dedicated-account-password")).Expect<SharpAccount>();
		await accounts.LinkCharacterAsync(account.Id!, character);
		var session = await sessions.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), "203.0.113.8");
		var authority = (await mediator.Send(new GetNativeCharacterAuthorityQuery(account.Id!, character))).Expect<NativeCharacterAuthority>();
		var grant = proofs.Issue(account.Id!, session, AccountSecurityOperation.NativeLogin, new("password", null, authority.AccountCredentialEpoch)).Expect<RecentAccountProof>();
		var mint = (await native.MintAsync(account.Id!, session, character, "203.0.113.8", grant.Grant)).Expect<NativeLoginSecretMint>();
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		var replay = TestIsolationHelpers.GenerateUniqueHandle();
		try
		{
			foreach (var descriptor in new[] { handle, replay })
				await connections.Register(descriptor, "203.0.113.9", "client.example.test", "telnet", _ => ValueTask.CompletedTask,
					_ => ValueTask.CompletedTask, () => Encoding.UTF8);
			await WebAppFactoryArg.CommandParser.CommandParse(handle, connections, MString.Plain($"connect token {mint.Secret}"));
			await Assert.That(connections.Get(handle)!.Ref).IsEqualTo(character);
			await Assert.That(connections.Get(handle)!.State).IsEqualTo(IConnectionService.ConnectionState.LoggedIn);
			await WebAppFactoryArg.CommandParser.CommandParse(replay, connections, MString.Plain($"connect token {mint.Secret}"));
			await Assert.That(connections.Get(replay)!.Ref).IsNull();
			await sessions.RevokeAsync(session);
			await Assert.That(connections.Get(handle)!.Ref).IsEqualTo(character);
		}
		finally
		{
			await connections.Disconnect(handle);
			await connections.Disconnect(replay);
			await sessions.RevokeAllForAccountAsync(account.Id!);
		}
	}
}
