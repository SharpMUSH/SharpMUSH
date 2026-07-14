using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Services;

public class SetupAutoCompleteTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test, NotInParallel("ServerStateTests")] // shares the ServerState doc with ServerStateTests
	public async Task SettingGodsPassword_CompletesSetup()
	{
		await Db.SetServerSetupCompletedAsync(false);

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var god = one.AsPlayer;
		// God has no password by default (see RoomContentsEventTests) — capture it so later
		// tests that rely on password-less God login aren't broken by this test.
		var originalPasswordHash = god.PasswordHash;
		var originalPasswordSalt = god.PasswordSalt;

		await Mediator.Send(new SetPlayerPasswordCommand(god, "hashed-anything"));

		await Assert.That((await Db.GetServerStateAsync()).SetupCompleted).IsTrue();

		// Restore God's original (unhashed/no-op) password. Passing a non-null salt makes the
		// handler treat the password as already-hashed, so this writes the original hash back
		// verbatim instead of re-hashing it.
		await Db.SetPlayerPasswordAsync(god, originalPasswordHash, originalPasswordSalt ?? "");

		await Db.SetServerSetupCompletedAsync(false); // restore for setup-flow tests
	}

	[Test, NotInParallel("ServerStateTests")]
	public async Task ServerState_MediatorRoundTrip()
	{
		await Mediator.Send(new SetServerSetupCompletedCommand(false));
		var state = await Mediator.Send(new GetServerStateQuery());
		await Assert.That(state.SetupCompleted).IsFalse();
	}
}
