using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// An imported configuration's command and function aliases are checked against this engine before they join the
/// alias options: what they name has to be a built-in here, the alias has to be free, and one already in force is
/// left out without a note. Everything else is reported.
/// </summary>
public class ConfiguredAliasImportTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ConfiguredAliasImport Aliases => WebAppFactoryArg.Services.GetRequiredService<ConfiguredAliasImport>();

	[Test]
	public async Task ACommandAliasNamesTheBuiltInAndItsSwitches()
	{
		var resolved = Aliases.Resolve(Import(
			Command("@sw2", "@switch"),
			Command("@qf", "@force/inplace"),
			Command("@sw", "@switch")), ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Commands["@SWITCH"]).IsEquivalentTo(new[] { "@sw2" });
		await Assert.That(resolved.Commands["@FORCE/INPLACE"]).IsEquivalentTo(new[] { "@qf" });
		await Assert.That(resolved.NotCarried).IsEmpty()
			.Because("@sw is already SharpMUSH's alias of @switch, so nothing is lost");
	}

	[Test]
	public async Task ACommandAliasThatCannotBeKeptIsReported()
	{
		var resolved = Aliases.Resolve(Import(
			Command("@nope", "@nosuchcommand"),
			Command("@qn", "@force/nosuchswitch"),
			Command("look", "@emit")), ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Commands).IsEmpty();
		await Assert.That(resolved.NotCarried).IsEquivalentTo(new[]
		{
			"alias @nope @nosuchcommand: SharpMUSH has no command @nosuchcommand, so it was not carried over.",
			"alias @qn @force/nosuchswitch: SharpMUSH's @FORCE has no /nosuchswitch switch, so it was not carried over.",
			"alias look @emit: look is already SharpMUSH's LOOK, so it was not carried over."
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// A target the dialect names differently is found through the dialect: TinyMUX's secs2time is convsecs. The
	/// alias is keyed by the built-in's own name, in the case the engine registers it under.
	/// </summary>
	[Test]
	public async Task AFunctionAliasIsFoundThroughTheDialect()
	{
		var resolved = Aliases.Resolve(Import(
			Function("plusser", "add"),
			Function("s2t", "secs2time"),
			Function("strlen", "add")), ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Functions["add"]).IsEquivalentTo(new[] { "plusser" });
		await Assert.That(resolved.Functions["CONVSECS"]).IsEquivalentTo(new[] { "s2t" });
		await Assert.That(resolved.NotCarried).IsEquivalentTo(new[]
		{
			"function_alias strlen add: strlen() is already SharpMUSH's strlen(), so it was not carried over."
		});
	}

	/// <summary>TinyMUX and RhostMUSH keep the first line for an alias; a later one naming something else is reported.</summary>
	[Test]
	public async Task ASecondLineForAnAliasIsReported()
	{
		var resolved = Aliases.Resolve(Import(
			Function("plusser", "add"),
			Function("plusser", "sub")), ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Functions.Keys).IsEquivalentTo(new[] { "add" });
		await Assert.That(resolved.NotCarried.Single()).StartsWith("function_alias plusser sub: an earlier line");
	}

	[Test]
	public async Task TheAliasesJoinTheOnesTheGameHas()
	{
		var resolved = Aliases.Resolve(Import(Command("@sw2", "@switch")), ConfigurationDialect.TinyMUX);

		var merged = resolved.Over(AliasOptions.Default);

		await Assert.That(merged.CommandAliases["@SWITCH"]).IsEquivalentTo(new[] { "@sw", "@sw2" });
		await Assert.That(merged.FunctionAliases).IsEquivalentTo(AliasOptions.Default.FunctionAliases);
	}

	[Test]
	public async Task ARestrictionOnANameThisEngineLacksIsReported()
	{
		var import = Import() with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(
					new Dictionary<string, string[]> { ["@boot"] = ["wizard"], ["@nosuchcommand"] = ["wizard"] },
					new Dictionary<string, string[]> { ["nosuchfunction"] = ["god"] })
			}
		};

		var resolved = Aliases.Resolve(import, ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.NotCarried).IsEquivalentTo(new[]
		{
			"The restriction on @nosuchcommand: SharpMUSH has no command @nosuchcommand, so it has no effect.",
			"The restriction on nosuchfunction(): SharpMUSH has no function nosuchfunction(), so it has no effect."
		});
	}

	/// <summary>
	/// A TinyMUX access line adds to the command's own requirements there, but a SharpMUSH restriction replaces the
	/// command's lock. So one on a command with a lock of its own here is not carried over unless it disables the
	/// command or leaves it to God; one on an open command, or on an alias the same file makes, stays.
	/// </summary>
	[Test]
	public async Task AnAccessLineThatWouldReplaceACommandsOwnLockIsNotCarried()
	{
		var import = Import(Command("@em2", "@emit")) with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(new Dictionary<string, string[]>
				{
					["@wizmotd"] = ["noguest"],
					["@emit"] = ["wizard"],
					["@em2"] = ["wizard"],
					["@motd"] = ["nobody"]
				}, new Dictionary<string, string[]>())
			}
		};

		var resolved = Aliases.Resolve(import, ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.CommandRestrictions.Keys).IsEquivalentTo(new[] { "@emit", "@em2", "@motd" });
		await Assert.That(resolved.NotCarried.Single()).StartsWith("The restriction on @wizmotd (noguest): SharpMUSH's @WIZMOTD has a restriction of its own");

		var penn = Aliases.Resolve(import, ConfigurationDialect.PennMUSH);
		await Assert.That(penn.CommandRestrictions.Keys).Contains("@wizmotd")
			.Because("PennMUSH's restrict_command replaces the command's restriction there too");
	}

	private static AliasDirective Command(string alias, string target) => new(AliasKind.Command, alias, target, $"alias {alias} {target}");

	private static AliasDirective Function(string alias, string target) => new(AliasKind.Function, alias, target, $"function_alias {alias} {target}");

	private static PennMushConfigImport Import(params AliasDirective[] aliases)
		=> new(SharpMUSHOptions.Default() with
		{
			Restriction = new RestrictionOptions(new Dictionary<string, string[]>(), new Dictionary<string, string[]>())
		}, [], new HashSet<string>(), new Dictionary<string, string>())
		{
			Aliases = aliases
		};
}
