using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// End-to-end regression for the <c>SetPlayerPasswordAsync</c> double-hash bug.
///
/// Every in-game/service caller of the password setter passes an ALREADY-HASHED value with
/// <c>salt == null</c> (<see cref="IPasswordService.SetPassword"/>,
/// <c>PasswordService.RehashPasswordAsync</c>, and the <c>@password</c>/<c>@newpassword</c>
/// commands all call <see cref="IPasswordService.HashPassword"/> first). The DB providers used to
/// re-hash that value (<c>Hash(Hash(plaintext))</c>), so at connect
/// <c>PasswordService.PasswordIsValid</c> — which hashes the plaintext exactly once — never
/// matched, and a character whose password was set via <c>@password</c> or
/// <see cref="IPasswordService.SetPassword"/> could never connect.
///
/// This drives the REAL caller→command→DB path (<see cref="IPasswordService.SetPassword"/> with a
/// pre-hashed value) and then simulates <c>connect &lt;name&gt; &lt;plaintext&gt;</c> on a fresh
/// pre-login socket, asserting the handle binds with the correct plaintext and does NOT bind with
/// a wrong one. With the re-hash restored (the bug), the correct-password assertion fails with
/// INVALID PASSWORD and the handle stays unbound.
/// </summary>
public class SetPasswordConnectTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IPasswordService PasswordService => WebAppFactoryArg.Services.GetRequiredService<IPasswordService>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	private static string PlainMessage(CallState result) => result.Message?.ToString() ?? "";

	private async ValueTask<long> RegisterConnectionAsync(long handle)
	{
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	[Test, NotInParallel(nameof(SetPasswordConnectTests))]
	public async ValueTask SetPassword_ThenConnectWithPlaintext_Binds()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("setpwconnect");
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;

		// Create the player with some initial password, then obtain the SharpPlayer via the
		// dbref-keyed object query (NOT the name-keyed GetPlayerQuery that connect uses, so we
		// don't cache a pre-set snapshot of the player's PasswordHash).
		var dbref = await Mediator.Send(new CreatePlayerCommand(name, "initial-throwaway-pw", defaultHome, defaultHome, startingQuota));
		var player = (await Mediator.Send(new GetObjectNodeQuery(dbref))).AsPlayer;

		// Set a NEW password the way production does: hash the plaintext first, then hand the
		// already-hashed value to the setter (salt == null). This exercises
		// caller → SetPlayerPasswordCommand → SetPlayerPasswordAsync exactly as @password does.
		const string plaintext = "brand-new-secret-42";
		var preHashed = PasswordService.HashPassword(player.Object.DBRef.ToString(), plaintext);
		await PasswordService.SetPassword(player, preHashed);

		// A WRONG password must NOT bind (guards against a vacuous pass).
		var wrongHandle = await RegisterConnectionAsync(6101L);
		var wrongResult = await Parser.CommandParse(wrongHandle, ConnectionService, MModule.single($"connect {name} definitely-not-it"));
		await Assert.That(PlainMessage(wrongResult)).IsEqualTo(ErrorMessages.Returns.InvalidPassword);
		await Assert.That(ConnectionService.Get(wrongHandle)?.Ref).IsNull();

		// The CORRECT plaintext must bind. With the double-hash bug this fails: the stored value is
		// Hash(preHashed), PasswordIsValid hashes the plaintext once, they never match, and the
		// handle stays unbound (INVALID PASSWORD).
		var rightHandle = await RegisterConnectionAsync(6102L);
		var rightResult = await Parser.CommandParse(rightHandle, ConnectionService, MModule.single($"connect {name} {plaintext}"));
		await Assert.That(PlainMessage(rightResult).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(rightHandle)?.Ref).IsNotNull();
	}
}
