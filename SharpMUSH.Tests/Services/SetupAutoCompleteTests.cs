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

	// Serialized against ServerStateTests (shared ServerState doc) AND ConfigMutation: this test
	// temporarily mutates the *shared* God character's password, and the ConfigMutation-group
	// connect tests (e.g. LoginsConfigTests) authenticate as God — they must not observe the
	// transient "hashed-anything" value while it is set here.
	[Test, NotInParallel(["ServerStateTests", "ConfigMutation"])]
	public async Task SettingGodsPassword_CompletesSetup()
	{
		await Db.SetServerSetupCompletedAsync(false);

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var god = one.AsPlayer;
		// God has no password by default (see RoomContentsEventTests) — capture it so later
		// tests that rely on password-less God login aren't broken by this test.
		var originalPasswordHash = god.PasswordHash;
		var originalPasswordSalt = god.PasswordSalt;

		try
		{
			await Mediator.Send(new SetPlayerPasswordCommand(god, "hashed-anything"));

			await Assert.That((await Db.GetServerStateAsync()).SetupCompleted).IsTrue();
		}
		finally
		{
			// Restore God's original password/salt verbatim THROUGH the mediator command so the
			// object/name caches are invalidated to match. The mutation above (SetPlayerPasswordCommand)
			// evicts the God caches; a parallel test that reads God can then repopulate them with the
			// mutated value. A direct Db.SetPlayerPasswordAsync restore writes the DB but leaves that
			// stale value in cache for its whole TTL, breaking parallel God-login tests. Routing the
			// restore through the command re-invalidates, keeping cache and DB consistent. (Post the
			// double-hash fix the command stores the password verbatim, so the original hash is written
			// back without re-hashing.) Runs even if an assertion above threw, so a failed assertion
			// can't leave God's password (or SetupCompleted) mutated for later tests.
			await Mediator.Send(new SetPlayerPasswordCommand(god, originalPasswordHash, originalPasswordSalt));

			await Db.SetServerSetupCompletedAsync(false); // restore for setup-flow tests
		}
	}

	// See SettingGodsPassword_CompletesSetup — also serialized against the God-authenticating
	// ConfigMutation connect tests while it mutates the shared God password.
	[Test, NotInParallel(["ServerStateTests", "ConfigMutation"])]
	public async Task SettingGodsEmptyPassword_DoesNotCompleteSetup()
	{
		await Db.SetServerSetupCompletedAsync(false);

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var god = one.AsPlayer;
		var originalPasswordHash = god.PasswordHash;
		var originalPasswordSalt = god.PasswordSalt;

		try
		{
			// Re-unclaiming God (setting an empty password hash) must not flip SetupCompleted.
			await Mediator.Send(new SetPlayerPasswordCommand(god, string.Empty));

			await Assert.That((await Db.GetServerStateAsync()).SetupCompleted).IsFalse();
		}
		finally
		{
			// Restore through the command (not a direct DB write) so the God caches are re-invalidated
			// to match the DB — see SettingGodsPassword_CompletesSetup for the full rationale.
			await Mediator.Send(new SetPlayerPasswordCommand(god, originalPasswordHash, originalPasswordSalt));
			await Db.SetServerSetupCompletedAsync(false); // restore for setup-flow tests
		}
	}

	[Test, NotInParallel("ServerStateTests")]
	public async Task ServerState_MediatorRoundTrip()
	{
		await Mediator.Send(new SetServerSetupCompletedCommand(false));
		var state = await Mediator.Send(new GetServerStateQuery());
		await Assert.That(state.SetupCompleted).IsFalse();
	}
}
