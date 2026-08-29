using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A flag answers to its aliases as well as its name, because PennMUSH resolves a flag name through
/// <c>flag_hash_lookup</c> → <c>match_flag_ns</c> → <c>ptab_flag</c>, declared in <c>src/flags.c</c>
/// as "Table of flags by name, inc. aliases".
///
/// <para>Every aliased flag in the seed used to be reachable by exactly one of its spellings, and the
/// three places that answer the question disagreed about which: <c>HelperFunctions.HasFlag</c> and the
/// database pushdown predicate matched the name only, <c>hasflag()</c> matched name or letter, and
/// <c>HasPower</c> one screen away already matched a power's alias (issue #834).</para>
///
/// <para>MONITOR is the case with the most reach — its aliases LISTENER and WATCHER are how most
/// codebases spell it — but COLOR/COLOUR is the one players hit, so both are covered.</para>
/// </summary>
public class FlagAliasTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string expr)
		=> (await FunctionParser.FunctionParse(MModule.single(expr)))?.Message!.ToPlainText() ?? "<null>";

	private async Task<DBRef> ThingFlagged(string label, string flag)
	{
		var dbref = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, label);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@set {dbref}={flag}"));
		return dbref;
	}

	[Test]
	public async Task HasFlagFunction_AnswersToAnAlias()
	{
		var thing = await ThingFlagged("AliasMonitor", "MONITOR");

		await Assert.That(await Eval($"hasflag(#{thing.Number},MONITOR)"))
			.IsEqualTo("1")
			.Because("the canonical name has to work before the aliases mean anything");
		await Assert.That(await Eval($"hasflag(#{thing.Number},LISTENER)")).IsEqualTo("1");
		await Assert.That(await Eval($"hasflag(#{thing.Number},WATCHER)")).IsEqualTo("1");

		// The letter still resolves, as flag_hash_lookup's single-character fallback does.
		await Assert.That(await Eval($"hasflag(#{thing.Number},M)")).IsEqualTo("1");
	}

	/// <summary>
	/// COLOR is seeded <c>["PLAYER"]</c>, so this needs a player — setting it on a thing is correctly
	/// refused by the type restriction and would test nothing.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task HasFlagFunction_AnswersToTheBritishSpellingOfColour()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AliasColour");
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@set #{player.DbRef.Number}=COLOR"));

		await Assert.That(await Eval($"hasflag(#{player.DbRef.Number},COLOR)"))
			.IsEqualTo("1")
			.Because("the canonical name has to work before the alias means anything");
		await Assert.That(await Eval($"hasflag(#{player.DbRef.Number},COLOUR)")).IsEqualTo("1");
	}

	/// <summary>A second alias case on a flag that things may carry: TRUST is spelled INHERIT too.</summary>
	[Test]
	public async Task HasFlagFunction_AnswersToInheritForTrust()
	{
		var thing = await ThingFlagged("AliasTrust", "TRUST");

		await Assert.That(await Eval($"hasflag(#{thing.Number},TRUST)")).IsEqualTo("1");
		await Assert.That(await Eval($"hasflag(#{thing.Number},INHERIT)")).IsEqualTo("1");
	}

	/// <summary>
	/// The alias arm must resolve to its own flag and no other, or it would be worse than the gap it
	/// closes: a name that widens into "any flag" fails open on every gate built on it.
	/// </summary>
	[Test]
	public async Task HasFlagFunction_DoesNotAnswerToAnotherFlagsAlias()
	{
		var thing = await ThingFlagged("AliasNegative", "MONITOR");

		await Assert.That(await Eval($"hasflag(#{thing.Number},COLOUR)")).IsEqualTo("0");
		await Assert.That(await Eval($"hasflag(#{thing.Number},TEL-OK)")).IsEqualTo("0");
	}

	/// <summary>
	/// The helper behind every permission gate, not just the softcode function — they were separate
	/// implementations and disagreed.
	/// </summary>
	[Test]
	public async Task HasFlagHelper_AnswersToAnAlias()
	{
		var dbref = await ThingFlagged("AliasHelper", "MONITOR");
		var obj = (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;

		await Assert.That(await obj.HasFlag("MONITOR")).IsTrue();
		await Assert.That(await obj.HasFlag("LISTENER")).IsTrue();
		await Assert.That(await obj.HasFlag("WATCHER")).IsTrue();
		await Assert.That(await obj.HasFlag("COLOUR")).IsFalse();

		// IsListener() is HasFlag("Monitor"); reaching it by the alias is the point of the change.
		await Assert.That(await obj.IsListener()).IsTrue();
	}

	/// <summary>
	/// A flag with no aliases at all must keep working — the seed has plenty (DARK, WIZARD), and a
	/// null or empty alias list is the case an unguarded <c>.Any()</c> would throw on.
	/// </summary>
	[Test]
	public async Task HasFlagHelper_StillWorksForAFlagWithNoAliases()
	{
		var dbref = await ThingFlagged("AliasNone", "DARK");
		var obj = (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;

		await Assert.That(await obj.HasFlag("DARK")).IsTrue();
		await Assert.That(await obj.HasFlag("WIZARD")).IsFalse();
	}
}
