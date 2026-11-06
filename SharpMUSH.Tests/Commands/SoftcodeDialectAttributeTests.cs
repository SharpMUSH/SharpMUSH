using SharpMUSH.Database.Seed;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Code in a dialect names an attribute the way its server does, and finds it under the name the import stored it
/// by: TinyMUX's <c>get(%#/kill)</c> reads <c>DEATH</c>, and its <c>@kill me=...</c> sets it.
/// </summary>
public class SoftcodeDialectAttributeTests : ServerTestBase
{
	public sealed record AttributeAlias(string Dialect, string Name, string Target)
	{
		public override string ToString() => $"{Dialect}:{Name}";
	}

	public static IEnumerable<Func<AttributeAlias>> Aliases()
		=> SoftcodeDialects.All.Values
			.SelectMany(dialect => dialect.AttributeAliases.Select(alias => new AttributeAlias(dialect.Key, alias.Key, alias.Value)))
			.Select(alias => (Func<AttributeAlias>)(() => alias));

	[Test]
	public async Task MuxAliasesCoverEveryAttributeTheImportRenames()
	{
		foreach (var (muxName, sharpName) in TinyMuxNames.AttributeRenames)
		{
			await Assert.That(SoftcodeDialects.Mux.AttributeFor(muxName)).IsEqualTo(sharpName).Because(muxName);
		}
	}

	[Test]
	public async Task RhostAliasesCoverEveryAttributeTheImportRenames()
	{
		var renamed = RhostNameTables.RenamedAttributes
			.Select(rename => (Rhost: RhostNameTables.BuiltInAttributes[rename.Key].ToUpperInvariant(), Sharp: rename.Value))
			.Where(rename => rename.Rhost != rename.Sharp)
			.ToList();

		await Assert.That(renamed).IsNotEmpty();
		foreach (var (rhostName, sharpName) in renamed)
		{
			await Assert.That(SoftcodeDialects.Rhost.AttributeFor(rhostName)).IsEqualTo(sharpName).Because(rhostName);
		}
	}

	/// <summary>
	/// An alias means one of SharpMUSH's standard attributes, and is never the name of one, so it reaches nothing
	/// SharpMUSH code could mean by it. Its <c>@</c>-command is not a dialect command alias either.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(Aliases))]
	public async Task AliasNamesAStandardAttributeAndIsNotOne(AttributeAlias alias)
	{
		var entries = AttributeEntrySeed.Entries.Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var dialect = SoftcodeDialects.All[alias.Dialect];

		await Assert.That(entries).Contains(alias.Target);
		await Assert.That(alias.Target).IsEqualTo(alias.Target.ToUpperInvariant());
		await Assert.That(entries.Contains(alias.Name)).IsFalse();
		await Assert.That(dialect.CommandFor("@" + alias.Name)).IsNull();
	}

	[Test]
	[MethodDataSource(nameof(Aliases))]
	public async Task FlaggedAttributeReadsAliasAsItsTarget(AttributeAlias alias)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("DialectAttr");
		var thing = DBRef.Parse(await Cmd($"@create {name}"));
		var value = ValueFor(alias.Target, thing, name, TestIsolationHelpers.GenerateUniqueName("dialectvalue"));
		await Cmd($"&{alias.Target} {thing}={value}");
		await Cmd($"&FLAGGED {thing}=[get(me/{alias.Name.ToLowerInvariant()})]");
		await Cmd($"@set {thing}/FLAGGED={alias.Dialect}");

		await Assert.That(await Eval($"u({thing}/FLAGGED)")).IsEqualTo(value);
	}

	[Test]
	[MethodDataSource(nameof(Aliases))]
	public async Task FlaggedAttributeWritesAliasToItsTarget(AttributeAlias alias)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("DialectSet");
		var thing = DBRef.Parse(await Cmd($"@create {name}"));
		// Some targets only a wizard sets.
		await Cmd($"@set {thing}=WIZARD");
		var viaAmpersand = ValueFor(alias.Target, thing, name, TestIsolationHelpers.GenerateUniqueName("dialectamp"));
		var viaCommand = ValueFor(alias.Target, DBRef.Parse("#1"), name.ToUpperInvariant(), TestIsolationHelpers.GenerateUniqueName("dialectcmd"));
		await Cmd($"&AMPERSAND {thing}=&{alias.Name} me={viaAmpersand}");
		await Cmd($"@set {thing}/AMPERSAND={alias.Dialect}");
		await Cmd($"&COMMAND {thing}=@{alias.Name.ToLowerInvariant()} me={viaCommand}");
		await Cmd($"@set {thing}/COMMAND={alias.Dialect}");

		await Cmd($"@trigger {thing}/AMPERSAND");
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(await Eval($"get({thing}/{alias.Target})")).IsEqualTo(viaAmpersand);

		await Cmd($"@trigger {thing}/COMMAND");
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(await Eval($"get({thing}/{alias.Target})")).IsEqualTo(viaCommand);
		await Assert.That(await Eval($"member(lattr({thing}),{alias.Name})")).IsEqualTo("0");
	}

	/// <summary>A value <paramref name="target"/> takes: a moniker is the name, a forward list a dbref.</summary>
	private static string ValueFor(string target, DBRef thing, string name, string otherwise) => target switch
	{
		"MONIKER" => name,
		"FORWARDLIST" => thing.ToString(),
		_ => otherwise
	};

	[Test]
	public async Task UnflaggedAttributeKeepsSharpMushNames()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectPlain")}"));
		await Cmd($"&DEATH {thing}=died");
		await Cmd($"&PLAIN {thing}=[get(me/kill)]");
		await Cmd($"&FLAGGED {thing}=[u(me/plain)]");
		await Cmd($"@set {thing}/FLAGGED=mux");

		await Assert.That(await Eval($"u({thing}/PLAIN)")).IsEqualTo(string.Empty);
		// A SharpMUSH attribute called from TinyMUX code still reads its own names.
		await Assert.That(await Eval($"u({thing}/FLAGGED)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task FlaggedAttributeCallsAliasedAttribute()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectUfun")}"));
		await Cmd($"&ADEATH {thing}=[add(%0,1)]");
		await Cmd($"&FLAGGED {thing}=[u(me/akill,41)] [hasattr(me,akill)] [lattr(me/akill)]");
		await Cmd($"@set {thing}/FLAGGED=mux");

		await Assert.That(await Eval($"u({thing}/FLAGGED)")).IsEqualTo("42 1 ADEATH");
	}

	/// <summary>An object's own attribute called <c>KILL</c> comes first, for a read and for a write.</summary>
	[Test]
	public async Task OwnAttributeOfTheAliasNameComesFirst()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectOwn")}"));
		await Cmd($"&KILL {thing}=native");
		await Cmd($"&DEATH {thing}=death");
		await Cmd($"&FLAGGED {thing}=[get(me/kill)]");
		await Cmd($"@set {thing}/FLAGGED=mux");
		await Cmd($"&WRITE {thing}=&kill me=rewritten");
		await Cmd($"@set {thing}/WRITE=mux");

		await Assert.That(await Eval($"u({thing}/FLAGGED)")).IsEqualTo("native");
		await Cmd($"@trigger {thing}/WRITE");
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(await Eval($"[get({thing}/KILL)]|[get({thing}/DEATH)]")).IsEqualTo("rewritten|death");
	}

	/// <summary>A pattern match moves the attributes it matched, by their stored names.</summary>
	[Test]
	public async Task PatternMoveKeepsStoredNames()
	{
		var source = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectSrc")}"));
		var destination = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectDst")}"));
		await Cmd($"&KILL {source}=native");
		await Cmd($"&DEATH {source}=death");
		await Cmd($"&FLAGGED {source}=@mvattr me/K*={destination}");
		await Cmd($"@set {source}/FLAGGED=mux");

		await Cmd($"@trigger {source}/FLAGGED");
		await WebAppFactoryArg.QueueBarrierAsync();

		await Assert.That(await Eval($"[get({destination}/KILL)]|[get({destination}/DEATH)]|[get({source}/KILL)]|[get({source}/DEATH)]"))
			.IsEqualTo("native|||death");
	}

	/// <summary><c>@mvattr</c> onto the same attribute under its other name moves nothing, so the value stays.</summary>
	[Test]
	public async Task MoveOntoItsOwnAliasKeepsTheValue()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectMove")}"));
		await Cmd($"&DEATH {thing}=kept");
		await Cmd($"&FLAGGED {thing}=@mvattr me/death=me/kill");
		await Cmd($"@set {thing}/FLAGGED=mux");

		await Cmd($"@trigger {thing}/FLAGGED");
		await WebAppFactoryArg.QueueBarrierAsync();

		await Assert.That(await Eval($"get({thing}/DEATH)")).IsEqualTo("kept");
	}

	/// <summary>A clone copies attributes by their stored names, so one called <c>KILL</c> stays <c>KILL</c>.</summary>
	[Test]
	public async Task CloneInDialectKeepsStoredNames()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("DialectClone");
		var thing = DBRef.Parse(await Cmd($"@create {name}"));
		await Cmd($"&KILL {thing}=native");
		await Cmd($"&DEATH {thing}=death");
		await Cmd($"&FLAGGED {thing}=[clone(me)]");
		await Cmd($"@set {thing}/FLAGGED=mux");

		var copy = DBRef.Parse(await Eval($"u({thing}/FLAGGED)"));

		await Assert.That(await Eval($"[get({copy}/KILL)]|[get({copy}/DEATH)]")).IsEqualTo("native|death");
	}

	/// <summary>TinyMUX's <c>@reject</c> sets the page refusal, not <c>@rejectmotd</c>, which SharpMUSH reads it as.</summary>
	[Test]
	public async Task GameDialectSetsAliasedAttributeByCommand()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectAtCmd");
		var reject = TestIsolationHelpers.GenerateUniqueName("dialectreject");
		var kill = TestIsolationHelpers.GenerateUniqueName("dialectkill");
		try
		{
			using (TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "mux" }
			}))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@reject me={reject}");
				await CmdAs(actor.DbRef, actor.Handle, $"@kill me={kill}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(await Eval($"get({actor.DbRef}/HAVEN)")).IsEqualTo(reject);
			await Assert.That(await Eval($"get({actor.DbRef}/DEATH)")).IsEqualTo(kill);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}
}
