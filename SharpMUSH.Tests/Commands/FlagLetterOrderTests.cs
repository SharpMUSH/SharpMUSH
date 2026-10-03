using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's <c>unparse_flags</c> (<c>src/flags.c:1638</c>): the type letter first, then every flag the
/// viewer may see in the flag table's bit order, CONNECTED (<c>c</c>) among them for a connected player
/// (#1408). <c>lflags()</c> is <c>bits_to_string</c>: the same order, by name, with no type.
/// </summary>
public class FlagLetterOrderTests : ServerTestBase
{
	private Task<TestIsolationHelpers.TestPlayer> NewPlayerAsync(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	[Test]
	public async Task AConnectedPlayerShowsItsTypeFirstAndConnectedInBitOrder()
	{
		var player = await NewPlayerAsync("FlagOrderPlayer");
		await Cmd($"@set #{player.DbRef.Number}=!no_command enter_ok ansi no_command");

		// enter_ok (e), no_command (n), ANSI (A), CONNECTED (c): Penn's Alice(#4PenAc).
		await Assert.That(await EvalAs(player.DbRef, "flags(me)")).IsEqualTo("PenAc");
		await Assert.That(await EvalAs(player.DbRef, "lflags(me)")).IsEqualTo("ENTER_OK NO_COMMAND ANSI CONNECTED");
	}

	[Test]
	public async Task ThingsAndRoomsShowTheirTypeLetterFirst()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("FlagOrderThing")})");
		await Cmd($"@set {thing}=no_command");
		await Assert.That(await Eval($"flags({thing})")).IsEqualTo("Tn");

		var room = await Eval($"dig({TestIsolationHelpers.GenerateUniqueName("FlagOrderRoom")})");
		await Cmd($"@set {room}=no_command link_ok");
		// LINK_OK comes before NO_COMMAND in Penn's bit order: Room Zero(#0RL...), never #0LR.
		await Assert.That(await Eval($"flags({room})")).IsEqualTo("RLn");
	}

	[Test]
	public async Task AHiddenConnectionShowsConnectedOnlyToPrivWho()
	{
		var hidden = await NewPlayerAsync("FlagOrderHidden");
		var mortal = await NewPlayerAsync("FlagOrderMortal");
		ConnectionService.Update(hidden.Handle, "Hidden", "1");

		await Assert.That(await EvalAs(mortal.DbRef, $"lflags(#{hidden.DbRef.Number})")).DoesNotContain("CONNECTED");
		await Assert.That(await Eval($"lflags(#{hidden.DbRef.Number})")).Contains("CONNECTED");
	}

	[Test]
	public async Task MdarkAndOdarkFlagsFollowCanSeeFlag()
	{
		var target = await NewPlayerAsync("FlagOrderTarget");
		var mortal = await NewPlayerAsync("FlagOrderOther");
		var reference = $"#{target.DbRef.Number}";
		// SUSPECT is mdark (wizards only), NOSPOOF odark (the owner's objects and wizards).
		await Cmd($"@set {reference}=suspect nospoof");

		await Assert.That(await Eval($"lflags({reference})")).Contains("SUSPECT").And.Contains("NOSPOOF");
		await Assert.That(await EvalAs(target.DbRef, "lflags(me)")).DoesNotContain("SUSPECT").And.Contains("NOSPOOF");
		await Assert.That(await EvalAs(mortal.DbRef, $"lflags({reference})")).DoesNotContain("SUSPECT").And.DoesNotContain("NOSPOOF");
	}
}
