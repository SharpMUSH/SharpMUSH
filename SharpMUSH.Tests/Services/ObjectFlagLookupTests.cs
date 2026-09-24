using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using Mediator;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// #1175: <c>HasQuietFlagAsync</c>, <c>HasNoWarnFlagAsync</c> and <c>IsGoingAsync</c> compared
/// <c>x.Name == "QUIET"</c> and friends - case-sensitive and blind to flag aliases - while every
/// other flag test in the codebase goes through <see cref="HelperFunctions.HasFlag"/>, which is
/// neither, as PennMUSH's <c>has_flag_by_name</c> -&gt; <c>flag_hash_lookup</c> -&gt;
/// <c>match_flag_ns</c> walks <c>ptab_flag</c>, "Table of flags by name, inc. aliases".
/// <para>
/// Flag names and aliases are admin-editable through <c>@flag/alias</c>, so the spellings a live
/// game can hold are not the ones the seed wrote. These are unit tests over hand-built objects
/// because that is the only way to put a stored spelling in front of the predicate - <c>@set</c>
/// resolves what you type back to the canonical definition before storing it.
/// </para>
/// </summary>
public class ObjectFlagLookupTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static AnySharpObject WithFlag(int key, string name, string flagName, string[]? aliases = null)
	{
		var obj = new TestObjectFactory().CreatePlayer(key, name);
		obj.Object().Flags = new(() => new[]
		{
			new SharpObjectFlag
			{
				Name = flagName,
				Aliases = aliases,
				Symbol = "?",
				SetPermissions = [],
				UnsetPermissions = [],
				TypeRestrictions = ["PLAYER"],
				System = false
			}
		}.ToAsyncEnumerable());
		return obj;
	}

	[Test]
	[Arguments("Quiet", null)]
	[Arguments("quiet", null)]
	[Arguments("SILENT", "QUIET")]
	public async Task HasQuietFlag_MatchesNameOrAliasCaselessly(string stored, string? alias)
	{
		var obj = WithFlag(9101, "QuietOne", stored, alias is null ? null : [alias]);

		await Assert.That(await obj.Object().HasQuietFlagAsync()).IsTrue();
	}

	[Test]
	[Arguments("No_Warn", null)]
	[Arguments("SHUSH", "NO_WARN")]
	public async Task HasNoWarnFlag_MatchesNameOrAliasCaselessly(string stored, string? alias)
	{
		var obj = WithFlag(9102, "WarnOne", stored, alias is null ? null : [alias]);

		await Assert.That(await obj.Object().HasNoWarnFlagAsync()).IsTrue();
	}

	[Test]
	[Arguments("Going", null)]
	[Arguments("DOOMED", "GOING")]
	public async Task IsGoing_MatchesNameOrAliasCaselessly(string stored, string? alias)
	{
		var obj = WithFlag(9103, "GoingOne", stored, alias is null ? null : [alias]);

		await Assert.That(await obj.Object().IsGoingAsync()).IsTrue();
	}

	/// <summary>
	/// <c>AreQuiet(player, thing)</c> reads the flag twice (<c>hdrs/dbdefs.h:198</c>), so both halves
	/// have to agree with <c>HasFlag</c>. The factory's player owns itself, which is what the second
	/// term needs.
	/// </summary>
	[Test]
	public async Task AreQuiet_ReadsBothHalvesThroughTheSameLookup()
	{
		var quietPlayer = WithFlag(9104, "QuietPlayer", "Quiet");
		var plainPlayer = new TestObjectFactory().CreatePlayer(9105, "PlainPlayer");
		var stranger = new TestObjectFactory().CreatePlayer(9107, "Stranger");

		// Owned by plainPlayer, not by itself: an object that owns itself would be answered by the
		// player half above and never reach the ownership term this is about.
		var quietThing = WithFlag(9106, "QuietThing", "SILENT", ["QUIET"]);
		quietThing.Object().Owner = new(async _ =>
		{
			await ValueTask.CompletedTask;
			return plainPlayer.Expect<SharpPlayer>();
		});

		await Assert.That(await plainPlayer.Object().AreQuietAsync(quietPlayer)).IsTrue()
			.Because("the player half is Quiet(x)");
		await Assert.That(await quietThing.Object().AreQuietAsync(plainPlayer)).IsTrue()
			.Because("the thing half is Quiet(y) && Owner(y) == x");
		await Assert.That(await quietThing.Object().AreQuietAsync(stranger)).IsFalse()
			.Because("Owner(y) == x needs the player to BE the owner, not merely to share one with it");
		await Assert.That(await plainPlayer.Object().AreQuietAsync(plainPlayer)).IsFalse()
			.Because("neither half holds, so the confirmation must still be printed");
	}

	/// <summary>
	/// End to end through the gate <c>AreQuietAsync</c> actually guards - <c>@set</c>'s
	/// "&lt;name&gt;/&lt;attribute&gt; - Set." confirmation (<c>SetHelpers.DoSet</c>, PennMUSH's
	/// <c>do_set_atr</c> <c>src/attrib.c:2446</c>) - as a mortal on their own object.
	/// </summary>
	[Test]
	public async Task AQuietMortalIsNotToldTheAttributeWasSet()
	{
		var quiet = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagQuietSet");
		var loud = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagLoudSet");

		// Control: an un-QUIET mortal IS told, so the silence below is the flag and not a @set that
		// never ran.
		var loudHeard = await Heard(loud, () => Parser.CommandParse(loud.Handle, ConnectionService,
			MarkupText.Plain("@set me=SEX:loud")).AsTask());
		await Assert.That(loudHeard).Contains(string.Format(
			ErrorMessages.Notifications.AttributeSet, loud.Name, "SEX"));

		await Parser.CommandParse(quiet.Handle, ConnectionService, MarkupText.Plain("@set me=QUIET"));

		var quietHeard = await Heard(quiet, () => Parser.CommandParse(quiet.Handle, ConnectionService,
			MarkupText.Plain("@set me=SEX:hush")).AsTask());

		await Assert.That(quietHeard).DoesNotContain(string.Format(
			ErrorMessages.Notifications.AttributeSet, quiet.Name, "SEX"));
	}

	private async Task<List<string>> Heard(TestIsolationHelpers.TestPlayer who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before)];
	}
}
