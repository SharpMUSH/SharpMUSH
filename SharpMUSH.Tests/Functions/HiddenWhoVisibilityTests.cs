using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The four WHO-family functions that #902 left gating on the DARK flag alone (issue #904):
/// <c>lwho()</c> and <c>lwhoid()</c> consulted only <c>PermissionService.CanSee</c>, <c>zmwho()</c>
/// checked the DARK flag by hand, and <c>zwho()</c> filtered nothing at all. All four listed a player
/// who had run <c>@hide</c> — per-connection Hidden state, PennMUSH's <c>DESC.hide</c>, which is not
/// the DARK flag and which the rest of the family (WHO, mwho, nwho, xwho, hidden()) already honored.
///
/// <para>Membership is asserted for this test's own players rather than list identity: the factory is
/// <see cref="SharedType.PerTestSession"/>, so other tests' connections come and go concurrently.</para>
/// </summary>
public class HiddenWhoVisibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<string> EvalAs(DBRef executor, string expression)
		=> (await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(expression)))
			?.Message!.ToPlainText() ?? "<null>";

	private async Task<string> God(string command)
		=> (await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)))?.Message?.ToPlainText() ?? "";

	/// <summary>
	/// Entries are <c>#N</c> for the dbref-shaped functions and full objids (<c>#N:creation</c>) for the
	/// id-shaped ones, so match on the <c>#N</c> token or its objid prefix rather than on the raw string —
	/// a bare <c>Contains</c> would let <c>#12</c> match <c>#123</c>.
	/// </summary>
	private static bool Lists(string result, DBRef who)
	{
		var needle = $"#{who.Number}";
		return result.Split(' ').Any(token => token == needle || token.StartsWith(needle + ":"));
	}

	private async Task<TestIsolationHelpers.TestPlayer> HiddenPlayer(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		ConnectionService.Update(player.Handle, "Hidden", "1");
		return player;
	}

	[Test]
	public async Task LwhoAndLwhoidHideAHiddenConnectionFromAnUnprivilegedLooker()
	{
		var hidden = await HiddenPlayer("LwhoHidden");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LwhoMortal");

		await Assert.That(Lists(await EvalAs(mortal.DbRef, "lwho()"), hidden.DbRef)).IsFalse()
			.Because("lwho() run by a mortal must drop an @hide'd connection");
		await Assert.That(Lists(await EvalAs(mortal.DbRef, "lwhoid()"), hidden.DbRef)).IsFalse()
			.Because("lwhoid() run by a mortal must drop an @hide'd connection");

		await Assert.That(Lists(await EvalAs(new DBRef(1), "lwho()"), hidden.DbRef)).IsTrue()
			.Because("a See_All looker still sees hidden connections");
		await Assert.That(Lists(await EvalAs(new DBRef(1), "lwhoid()"), hidden.DbRef)).IsTrue()
			.Because("a See_All looker still sees hidden connections");

		ConnectionService.Update(hidden.Handle, "Hidden", "0");
		await Assert.That(Lists(await EvalAs(mortal.DbRef, "lwho()"), hidden.DbRef)).IsTrue()
			.Because("clearing Hidden must restore the connection to the mortal's lwho()");
	}

	/// <summary>
	/// PennMUSH bsd.c:6540 (fun_lwho), :6501 (fun_nwho), :6446 (fun_xwho): an unprivileged caller may
	/// only ask about itself. Without that check the hidden gate is trivially bypassed — a mortal names
	/// a wizard as the viewer and gets the privileged answer back.
	/// </summary>
	[Test]
	public async Task AMortalCannotComputeTheWhoListForSomeoneElse()
	{
		var hidden = await HiddenPlayer("LwhoProxy");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LwhoProxyMortal");

		foreach (var call in (string[])["lwho(#1)", "lwhoid(#1)", "nwho(#1)", "xwho(#1,1,100000)", "xwhoid(#1,1,100000)"])
		{
			var result = await EvalAs(mortal.DbRef, call);
			await Assert.That(result).IsEqualTo("#-1 PERMISSION DENIED")
				.Because($"{call} must not hand a mortal God's WHO list");
			await Assert.That(Lists(result, hidden.DbRef)).IsFalse();
		}

		// The same call about itself is allowed, and is still unprivileged.
		await Assert.That(Lists(await EvalAs(mortal.DbRef, $"lwho({mortal.DbRef})"), hidden.DbRef)).IsFalse();
	}

	[Test]
	public async Task ZwhoRespectsHiddenAndZmwhoAlwaysDoes()
	{
		var zoneRoom = (await God($"@dig {TestIsolationHelpers.GenerateUniqueName("ZwhoZone")}")).Trim();
		var playerRoom = (await God($"@dig {TestIsolationHelpers.GenerateUniqueName("ZwhoRoom")}")).Trim();
		await God($"@chzone {playerRoom}={zoneRoom}");

		var hidden = await HiddenPlayer("ZwhoHidden");
		var visible = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ZwhoVisible");
		await God($"@teleport/quiet {hidden.DbRef}={playerRoom}");
		await God($"@teleport/quiet {visible.DbRef}={playerRoom}");

		var zwho = await EvalAs(new DBRef(1), $"zwho({zoneRoom})");
		var zmwho = await EvalAs(new DBRef(1), $"zmwho({zoneRoom})");

		await Assert.That(Lists(zwho, visible.DbRef)).IsTrue()
			.Because("zwho() must list a connected, visible player in the zone");
		await Assert.That(Lists(zmwho, visible.DbRef)).IsTrue()
			.Because("zmwho() must list a connected, visible player in the zone");

		await Assert.That(Lists(zwho, hidden.DbRef)).IsTrue()
			.Because("zwho() called by a See_All executor is powered, so it still sees hidden connections");
		await Assert.That(Lists(zmwho, hidden.DbRef)).IsFalse()
			.Because("zmwho() is the mortal-audience twin and is never powered (PennMUSH bsd.c:6815)");
	}

	/// <summary>
	/// zwho()'s second argument is the viewer whose visibility the answer is computed for — what
	/// <c>help zwho</c> has always documented, and what PennMUSH's fun_zwho does (bsd.c:6822). It was
	/// being read as an output separator instead, so there was no way to ask for the mortal view.
	/// </summary>
	[Test]
	public async Task ZwhoCapsTheAnswerAtTheNamedViewersPrivilege()
	{
		var zoneRoom = (await God($"@dig {TestIsolationHelpers.GenerateUniqueName("ZwhoViewZone")}")).Trim();
		var playerRoom = (await God($"@dig {TestIsolationHelpers.GenerateUniqueName("ZwhoViewRoom")}")).Trim();
		await God($"@chzone {playerRoom}={zoneRoom}");

		var hidden = await HiddenPlayer("ZwhoViewHidden");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ZwhoViewMortal");
		await God($"@teleport/quiet {hidden.DbRef}={playerRoom}");
		await God($"@teleport/quiet {mortal.DbRef}={playerRoom}");

		await Assert.That(Lists(await EvalAs(new DBRef(1), $"zwho({zoneRoom},{mortal.DbRef})"), hidden.DbRef))
			.IsFalse().Because("computing the answer for a mortal viewer must drop hidden connections");
		await Assert.That(Lists(await EvalAs(new DBRef(1), $"zwho({zoneRoom},{mortal.DbRef})"), mortal.DbRef))
			.IsTrue().Because("the mortal viewer can still see itself in the zone");

		await Assert.That(await EvalAs(mortal.DbRef, $"zwho({zoneRoom},#1)"))
			.IsEqualTo("#-1 PERMISSION DENIED")
			.Because("only a See_All caller may name a viewer other than itself");
	}

	/// <summary>
	/// zwho()/zmwho() walked the whole player table, so they answered for players who had never
	/// connected — <c>help zwho</c> says "currently-connected players", and PennMUSH iterates
	/// descriptors.
	/// </summary>
	[Test]
	public async Task ZwhoListsOnlyConnectedPlayers()
	{
		var zoneRoom = (await God($"@dig {TestIsolationHelpers.GenerateUniqueName("ZwhoConnZone")}")).Trim();
		var playerRoom = (await God($"@dig {TestIsolationHelpers.GenerateUniqueName("ZwhoConnRoom")}")).Trim();
		await God($"@chzone {playerRoom}={zoneRoom}");

		var offline = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "ZwhoOffline");
		await God($"@teleport/quiet {offline}={playerRoom}");

		await Assert.That(Lists(await EvalAs(new DBRef(1), $"zwho({zoneRoom})"), offline)).IsFalse();
		await Assert.That(Lists(await EvalAs(new DBRef(1), $"zmwho({zoneRoom})"), offline)).IsFalse();
	}
}
