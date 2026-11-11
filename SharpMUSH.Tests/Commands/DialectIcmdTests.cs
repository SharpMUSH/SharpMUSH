using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>TinyMUX's and RhostMUSH's <c>@icmd</c>, under their dialects.</summary>
public class DialectIcmdTests : ServerTestBase
{
	private static IDisposable Dialect(string dialect) => TestOptionsOverride.Scope(options => options with
	{
		Compatibility = options.Compatibility with { Dialect = dialect }
	});

	private async Task<(TestIsolationHelpers.TestPlayer Player, DBRef Room)> IsolatedPlayerAsync(string prefix)
	{
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		return (await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room), room);
	}

	/// <summary>A command of the test's own, so turning it off touches no other test.</summary>
	private async Task<string> CommandAsync()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("icmd").ToLowerInvariant();
		await Cmd($"@command/add {name}");
		return name;
	}

	private bool Heard(TestIsolationHelpers.TestPlayer player, string text)
		=> Notifications.For(player.DbRef).Any(line => line.Contains(text, StringComparison.Ordinal));

	[Test]
	[Arguments("mux")]
	[Arguments("rhost")]
	public async Task DisableRefusesIgnoreHidesAndRemovingRestores(string dialect)
	{
		var (player, _) = await IsolatedPlayerAsync("Icmd");
		var command = await CommandAsync();
		var upper = command.ToUpperInvariant();
		try
		{
			using (Dialect(dialect))
			{
				await Cmd($"@icmd {player.DbRef}={command}");
				await CmdAs(player.DbRef, player.Handle, command);
				await Assert.That(Heard(player, $"Permission denied. {upper} is turned off for you.")).IsTrue();

				await Cmd($"@icmd/ignore {player.DbRef}=!{command}, {command}");
				await CmdAs(player.DbRef, player.Handle, command);
				await Assert.That(Heard(player, "Huh?")).IsTrue();
				await Assert.That(await EvalAs(player.DbRef, "get(me/ICMD)")).DoesNotContain(upper);

				await Cmd($"@icmd/off {player.DbRef}");
				await CmdAs(player.DbRef, player.Handle, command);
			}

			await Assert.That(Heard(player, "This command has not been implemented.")).IsTrue();

			using (Dialect(dialect))
				await Cmd($"@icmd/clear {player.DbRef}");
			await Assert.That(await Eval($"hasattr({player.DbRef},ICMD)")).IsEqualTo("0");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async Task ARoomsSettingReachesEveryoneInIt()
	{
		var (player, room) = await IsolatedPlayerAsync("IcmdRoom");
		var command = await CommandAsync();
		try
		{
			using (Dialect("mux"))
			{
				await Cmd($"@icmd/droom {room}={command}");
				await CmdAs(player.DbRef, player.Handle, command);
				await Cmd($"@icmd/croom {room}");
			}

			await Assert.That(Heard(player, $"Permission denied. {command.ToUpperInvariant()} is turned off for you.")).IsTrue();
			await Assert.That(await Eval($"hasattr({room},ICMD)")).IsEqualTo("0");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>Only God sets it, and an ICMD attribute @icmd did not write changes nothing.</summary>
	[Test]
	public async Task OnlyGodSetsItAndAHandWrittenListIsIgnored()
	{
		var (player, _) = await IsolatedPlayerAsync("IcmdMortal");
		var command = await CommandAsync();
		try
		{
			using (Dialect("rhost"))
				await CmdAs(player.DbRef, player.Handle, $"@icmd me={command}");
			await Assert.That(Heard(player, "Permission denied")).IsTrue();

			await CmdAs(player.DbRef, player.Handle, $"&ICMD me=disable:{command.ToUpperInvariant()}");
			await CmdAs(player.DbRef, player.Handle, command);
			await Assert.That(Heard(player, "This command has not been implemented.")).IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>A wizard-flagged ICMD a wizard wrote is not @icmd's, so it changes nothing.</summary>
	[Test]
	public async Task AnIcmdAWizardWroteIsIgnored()
	{
		var (wizard, _) = await IsolatedPlayerAsync("IcmdWizard");
		var (player, _) = await IsolatedPlayerAsync("IcmdVictim");
		var command = await CommandAsync();
		try
		{
			await Cmd($"@set {wizard.DbRef}=WIZARD");
			await CmdAs(wizard.DbRef, wizard.Handle, $"&ICMD {player.DbRef}=disable:{command.ToUpperInvariant()}");
			await CmdAs(wizard.DbRef, wizard.Handle, $"@set {player.DbRef}/ICMD=wizard");
			await Assert.That(await Eval($"hasflag({player.DbRef}/ICMD,wizard)")).IsEqualTo("1");

			await CmdAs(player.DbRef, player.Handle, command);
			await Assert.That(Heard(player, "This command has not been implemented.")).IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(wizard.Handle);
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>Single-character commands never reach the check, so they are refused; an entry is removed by its stored name.</summary>
	[Test]
	public async Task SingleCharacterCommandsAreRefusedAndStoredNamesRemoved()
	{
		var (player, _) = await IsolatedPlayerAsync("IcmdToken");
		try
		{
			using (Dialect("mux"))
				await Cmd($"@icmd {player.DbRef}=&");
			await Assert.That(await Eval($"hasattr({player.DbRef},ICMD)")).IsEqualTo("0");

			var gone = TestIsolationHelpers.GenerateUniqueName("gone").ToUpperInvariant();
			await Cmd($"&ICMD {player.DbRef}=disable:{gone} disable:PAGE");
			await Cmd($"@set {player.DbRef}/ICMD=wizard");
			using (Dialect("mux"))
				await Cmd($"@icmd {player.DbRef}=!{gone.ToLowerInvariant()}");
			await Assert.That(await Eval($"get({player.DbRef}/ICMD)")).IsEqualTo("disable:PAGE");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}
}
