using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// A game's own alias and access lines are read from its configuration file: PennMUSH's <c>command_alias</c> and
/// <c>function_alias</c>, and TinyMUX's and RhostMUSH's <c>alias</c>, <c>function_alias</c>, <c>flag_alias</c>,
/// <c>power_alias</c>, <c>access</c> and <c>function_access</c>. A line SharpMUSH has nothing for is reported with
/// the reason, not lumped in with the directives it does not know.
/// </summary>
public class ReadConfigAliasAndAccessTests
{
	[Test]
	public async Task PennMushWritesTheTargetFirst()
	{
		var import = await ImportAsync(ConfigurationDialect.PennMUSH,
			"command_alias @switch @sw",
			"function_alias modulo modulus",
			"attribute_alias DESCRIBE DSC",
			"command_alias lonely");

		await Assert.That(import.Aliases).IsEquivalentTo(new[]
		{
			new AliasDirective(AliasKind.Command, "@sw", "@switch", "command_alias @switch @sw"),
			new AliasDirective(AliasKind.Function, "modulus", "modulo", "function_alias modulo modulus")
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(import.Skipped).Contains(
			"attribute_alias DESCRIBE DSC: SharpMUSH keeps no per-game attribute aliases, so it was not carried over.");
		await Assert.That(import.Skipped).Contains(
			"command_alias lonely: an alias line names a name and what it stands for, so it was not carried over.");
		await Assert.That(import.Unrecognized).IsEmpty();
	}

	[Test]
	public async Task TinyMuxWritesTheAliasFirst()
	{
		var import = await ImportAsync(ConfigurationDialect.TinyMUX,
			"alias @cc @mail/cc",
			"function_alias secs2t secs2time",
			"flag_alias ab abode",
			"power_alias ann=announce",
			"attr_alias kill akill");

		await Assert.That(import.Aliases).IsEquivalentTo(new[]
		{
			new AliasDirective(AliasKind.Command, "@cc", "@mail/cc", "alias @cc @mail/cc"),
			new AliasDirective(AliasKind.Function, "secs2t", "secs2time", "function_alias secs2t secs2time"),
			new AliasDirective(AliasKind.Flag, "ab", "abode", "flag_alias ab abode"),
			new AliasDirective(AliasKind.Power, "ann", "announce", "power_alias ann=announce")
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(import.Skipped.Any(line => line.StartsWith("attr_alias kill akill: "))).IsTrue();
		await Assert.That(import.Unrecognized).IsEmpty();
	}

	/// <summary>RhostMUSH has no power_alias, so in its file the line is a directive SharpMUSH does not know.</summary>
	[Test]
	public async Task RhostMushHasNoPowerAlias()
	{
		var import = await ImportAsync(ConfigurationDialect.RhostMUSH, "power_alias ann announce", "flag_alias ab abode");

		await Assert.That(import.Aliases.Select(alias => alias.Kind)).IsEquivalentTo(new[] { AliasKind.Flag });
		await Assert.That(import.Unrecognized).IsEquivalentTo(new[] { "power_alias" });
	}

	/// <summary>
	/// An access line's words become the restriction words SharpMUSH has for them, the lines for one name adding up,
	/// and a word may be abbreviated as far as TinyMUX's table allows. What has no restriction word is reported.
	/// </summary>
	[Test]
	public async Task TinyMuxAccessLinesBecomeRestrictions()
	{
		var import = await ImportAsync(ConfigurationDialect.TinyMUX,
			"access @boot wiz no_slave",
			"access @dig builder",
			"access @dig no_guest",
			"access @ps disabled",
			"function_access lwho god",
			"function_access lstats builder");

		var restrictions = import.Options.Restriction;
		await Assert.That(restrictions.CommandRestrictions["@boot"]).IsEquivalentTo(new[] { "wizard" });
		await Assert.That(restrictions.CommandRestrictions["@dig"]).IsEquivalentTo(new[] { "builder noguest" });
		await Assert.That(restrictions.CommandRestrictions["@ps"]).IsEquivalentTo(new[] { "nobody" });
		await Assert.That(restrictions.FunctionRestrictions["lwho"]).IsEquivalentTo(new[] { "god" });
		await Assert.That(restrictions.FunctionRestrictions.ContainsKey("lstats")).IsFalse()
			.Because("builder is the only word that says who may call it, and a function has no restriction for it");
		await Assert.That(import.Skipped).Contains(
			"access @boot wiz no_slave: carried over as wizard; SharpMUSH has no restriction for no_slave.");
		await Assert.That(import.Skipped).Contains(
			"function_access lstats builder: SharpMUSH has no restriction for builder, so it was not carried over and SharpMUSH's own restriction stays.");
		await Assert.That(import.Unrecognized).IsEmpty();
	}

	/// <summary>
	/// A line whose only words saying who may use the command have no restriction here is not carried over: leaving
	/// them out would open the command to everyone. A word clearing a permission loosens TinyMUX's own default, and a
	/// switch is not something SharpMUSH restricts; both are reported.
	/// </summary>
	[Test]
	public async Task AnAccessLineThatWouldWidenACommandIsNotCarried()
	{
		var import = await ImportAsync(ConfigurationDialect.TinyMUX,
			"access @admin staff no_guest",
			"access @find !wizard",
			"access @mail/cc wizard",
			"attr_access desc wizard");

		await Assert.That(import.Options.Restriction.CommandRestrictions.Keys)
			.DoesNotContain("@admin").And.DoesNotContain("@find").And.DoesNotContain("@mail/cc").And.DoesNotContain("@mail");
		await Assert.That(import.Skipped).Contains(
			"access @admin staff no_guest: SharpMUSH has no restriction for staff, so it was not carried over and SharpMUSH's own restriction stays.");
		await Assert.That(import.Skipped.Single(line => line.StartsWith("access @find !wizard: "))).Contains("loosens");
		await Assert.That(import.Skipped).Contains(
			"access @mail/cc wizard: SharpMUSH restricts a command, not one of its switches, so it was not carried over.");
		await Assert.That(import.Skipped.Any(line => line.StartsWith("attr_access desc wizard: "))).IsTrue();
	}

	/// <summary>
	/// RhostMUSH's royalty is its wizard level. Its shortest spellings differ from TinyMUX's: <c>ro</c> is too short
	/// for royalty, so it is robot.
	/// </summary>
	[Test]
	public async Task RhostMushAccessWordsAreItsOwn()
	{
		var import = await ImportAsync(ConfigurationDialect.RhostMUSH,
			"access @wall roy",
			"access @pcreate councilor",
			"access @nuke ro");

		var restrictions = import.Options.Restriction.CommandRestrictions;
		await Assert.That(restrictions["@wall"]).IsEquivalentTo(new[] { "wizard" });
		await Assert.That(restrictions.ContainsKey("@pcreate")).IsFalse();
		await Assert.That(restrictions.ContainsKey("@nuke")).IsFalse();
		await Assert.That(import.Skipped).Contains(
			"access @nuke ro: SharpMUSH has no restriction for robot, so it was not carried over and SharpMUSH's own restriction stays.");
	}

	/// <summary>PennMUSH has no access directive; in a mush.cnf it is one SharpMUSH does not know.</summary>
	[Test]
	public async Task AMushCnfHasNoAccessDirective()
	{
		var import = await ImportAsync(ConfigurationDialect.PennMUSH, "access @boot wizard");

		await Assert.That(import.Options.Restriction.CommandRestrictions.ContainsKey("@boot")).IsFalse();
		await Assert.That(import.Unrecognized).IsEquivalentTo(new[] { "access" });
	}

	private static async Task<PennMushConfigImport> ImportAsync(ConfigurationDialect dialect, params string[] lines)
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-conf-{Guid.NewGuid():N}.conf");
		await File.WriteAllLinesAsync(path, ["mud_name Aliased", .. lines]);
		try
		{
			return ReadPennMushConfig.Import(path, followIncludes: false, dialect);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
