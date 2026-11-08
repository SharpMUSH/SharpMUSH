using SharpMUSH.Configuration;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The <c>flag_alias</c> and <c>power_alias</c> lines of a TinyMUX game's configuration reach the flags and powers they
/// name, found by TinyMUX's names for them. An alias that already finds its flag here is left out without a note;
/// the rest that cannot be kept are reported with the reason.
/// </summary>
public class ConfiguredFlagAliasImportTests
{
	[Test]
	public async Task ConfiguredAliasesReachTheFlagsAndPowersTheyName()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Fixture(
			Flag("hd", "head"),
			Flag("ab", "abode"),
			Flag("zz", "abode"),
			Flag("imm", "immortal"),
			Flag("gn", "going"),
			Flag("nf", "nosuchflag"),
			Flag("dark", "head"),
			Power("ctl", "control_all"),
			Power("pg", "prog")));
		await Assert.That(result.Errors).IsEmpty();

		var head = await world.Mediator.Send(new GetObjectFlagQuery("HEAD"));
		await Assert.That(head!.Aliases).IsEquivalentTo(new[] { "hd" });
		await Assert.That((await world.Mediator.Send(new GetObjectFlagQuery("hd")))!.Name).IsEqualTo("HEAD");
		var prog = await world.Mediator.Send(new GetPowerQuery("prog"));
		await Assert.That(prog!.Aliases).IsEquivalentTo(new[] { "pg" });

		var warnings = result.Warnings.Texts().Where(text => text.StartsWith("Configuration line ")).ToList();
		await Assert.That(warnings.Any(text => text.Contains("flag_alias ab abode"))).IsFalse()
			.Because("ab already finds ABODE here");
		await Assert.That(warnings).Contains(
			"Configuration line flag_alias zz abode: ABODE is a built-in flag, which keeps SharpMUSH's own names, so it was not carried over");
		await Assert.That(warnings.Single(text => text.Contains("flag_alias imm immortal"))).Contains("Immortal power");
		await Assert.That(warnings.Single(text => text.Contains("flag_alias gn going"))).Contains("does not keep TinyMUX's GOING flag");
		await Assert.That(warnings).Contains(
			"Configuration line flag_alias nf nosuchflag: SharpMUSH has no flag nosuchflag, so it was not carried over");
		await Assert.That(warnings).Contains(
			"Configuration line flag_alias dark head: dark is already the DARK flag here, so it was not carried over");
		await Assert.That(warnings.Single(text => text.Contains("power_alias ctl control_all"))).Contains("control.all permission");
		await Assert.That(warnings).Count().IsEqualTo(6);
	}

	private static AliasDirective Flag(string alias, string target) => new(AliasKind.Flag, alias, target, $"flag_alias {alias} {target}");

	private static AliasDirective Power(string alias, string target) => new(AliasKind.Power, alias, target, $"power_alias {alias} {target}");

	private static PennMUSHDatabase Fixture(params AliasDirective[] aliases) => new()
	{
		Version = "Configured Alias Fixture",
		Dialect = DatabaseDialect.TinyMUX,
		FlagDefinitions = [new PennMUSHFlagDefinition { Name = "HEAD", Letter = "", Types = ["PLAYER"] }],
		PowerDefinitions = [new PennMUSHFlagDefinition { Name = "prog", Types = ["PLAYER"], SetPermissions = ["wizard"] }],
		ConfiguredAliases = [.. aliases],
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0 },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 }
		]
	};
}
