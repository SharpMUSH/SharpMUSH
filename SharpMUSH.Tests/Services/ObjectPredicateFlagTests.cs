using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Every <c>HelperFunctions</c> predicate that PennMUSH defines as a flag, asked of an object that
/// carries that flag, through the ordinary <c>@set</c> path and a real database.
///
/// <para>VISUAL, DARK, LIGHT, AUDIBLE, ORPHAN and PUPPET are <c>has_flag_by_name</c> calls in
/// <c>hdrs/dbdefs.h:132-162</c> and are seeded as flags by all three providers. The predicates asked
/// <c>Powers</c>, which has never held an entry by any of those names, so each one returned false for
/// every object that has ever existed — DARK hid nothing from <c>look</c> or <c>WHO</c>, VISUAL
/// granted no examine, and <c>IsAlive()</c>'s puppet and audible terms never fired (issue #796).</para>
///
/// <para>Each case asserts the softcode view and the predicate together. A predicate wired to the
/// wrong collection fails <em>silently</em>: <c>hasflag()</c> keeps saying 1 while the gate that flag
/// exists to drive never closes, so asserting either half alone would have passed throughout.</para>
/// </summary>
public class ObjectPredicateFlagTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task<string> EvalAs(DBRef executor, string expr)
		=> (await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MModule.single(expr)))
			?.Message!.ToPlainText() ?? "<null>";

	private async Task<AnySharpObject> ThingWithFlag(string label, string flag)
	{
		var dbref = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, label);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@set {dbref}={flag}"));

		var softcode = (await FunctionParser.FunctionParse(MModule.single($"hasflag({dbref},{flag})")))
			?.Message!.ToPlainText();
		await Assert.That(softcode)
			.IsEqualTo("1")
			.Because($"the {flag} flag has to actually be set before the predicate means anything");

		return (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;
	}

	[Test]
	public async Task IsDark_ReadsTheDarkFlag()
	{
		var dark = await ThingWithFlag("PredDark", "DARK");
		await Assert.That(await dark.IsDark()).IsTrue();

		// DarkLegal(x) = Dark(x) && (Can_Dark(x) || !Alive(x)). A plain thing is not alive, so a DARK
		// thing is legally dark — the whole term the look and locate gates consult.
		await Assert.That(await dark.IsDarkLegal()).IsTrue();
	}

	[Test]
	public async Task IsDark_IsFalseWithoutTheFlag()
	{
		var plain = (await Mediator.Send(new GetObjectNodeQuery(
			await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "PredNotDark")))).Known;

		await Assert.That(await plain.IsDark()).IsFalse();
		await Assert.That(await plain.IsDarkLegal()).IsFalse();
	}

	[Test]
	public async Task IsLight_ReadsTheLightFlag()
		=> await Assert.That(await (await ThingWithFlag("PredLight", "LIGHT")).IsLight()).IsTrue();

	[Test]
	public async Task IsVisual_ReadsTheVisualFlag()
		=> await Assert.That(await (await ThingWithFlag("PredVisual", "VISUAL")).IsVisual()).IsTrue();

	[Test]
	public async Task IsAudible_ReadsTheAudibleFlag()
		=> await Assert.That(await (await ThingWithFlag("PredAudible", "AUDIBLE")).IsAudible()).IsTrue();

	[Test]
	public async Task IsOrphan_ReadsTheOrphanFlag()
		=> await Assert.That(await (await ThingWithFlag("PredOrphan", "ORPHAN")).IsOrphan()).IsTrue();

	[Test]
	public async Task IsPuppet_ReadsThePuppetFlag()
	{
		var puppet = await ThingWithFlag("PredPuppet", "PUPPET");
		await Assert.That(await puppet.IsPuppet()).IsTrue();

		// IsAlive() is IsPlayer || IsPuppet || (IsAudible && FORWARDLIST) — and while IsPuppet and
		// IsAudible were both asked of Powers, the only live term was IsPlayer, so every thing and room
		// in the game was unconditionally "dead".
		await Assert.That(await puppet.IsAlive()).IsTrue();
	}

	/// <summary>
	/// The reach, not just the predicate: <c>fun_locate</c>'s own visibility check
	/// (<c>DbrefFunctions.cs</c>) is written around <c>DarkLegal</c> and could not refuse anything while
	/// that term was permanently false. A mortal now cannot locate a DARK object it has no standing
	/// over; a plain one in the same place it still can, which is what shows the refusal is the dark
	/// gate and not the search failing.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task ADarkObjectIsNotLocatableByAMortalSharingItsRoom()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DarkLocate");
		var mortalLoc = (await EvalAs(mortal.DbRef, "loc(%#)")).Split(':')[0];

		var darkName = TestIsolationHelpers.GenerateUniqueName("DarkLocateHidden");
		var plainName = TestIsolationHelpers.GenerateUniqueName("DarkLocateVisible");
		var dark = DBRef.Parse((await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@create {darkName}")))!.Message!.ToPlainText());
		var plain = DBRef.Parse((await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@create {plainName}")))!.Message!.ToPlainText());

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@set #{dark.Number}=DARK"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@tel #{dark.Number}={mortalLoc}"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@tel #{plain.Number}={mortalLoc}"));

		await Assert.That(await EvalAs(mortal.DbRef, $"locate(%#,{plainName},*)"))
			.IsEqualTo($"#{plain.Number}")
			.Because("the mortal has to be able to find an ordinary object here for the refusal below to mean anything");
		await Assert.That(await EvalAs(mortal.DbRef, $"locate(%#,{darkName},*)")).IsEqualTo("#-1");

		// God is See_All, so the same object is still reachable by someone entitled to see it.
		await Assert.That(await EvalAs(new DBRef(1), $"locate(#{mortal.DbRef.Number},{darkName},*)"))
			.IsEqualTo($"#{dark.Number}");
	}

	/// <summary>
	/// The powers next door are genuinely powers in PennMUSH (<c>Can_Dark</c>, <c>See_All</c>) and were
	/// right as they stood. This pins the boundary so a future sweep does not convert them too.
	/// </summary>
	[Test]
	public async Task CanDark_StaysAPowerQuestion()
	{
		var dark = await ThingWithFlag("PredCanDark", "DARK");

		// Setting DARK grants no Can_Dark; a thing has neither the power nor wizard standing.
		await Assert.That(await dark.CanDark()).IsFalse();
	}
}
