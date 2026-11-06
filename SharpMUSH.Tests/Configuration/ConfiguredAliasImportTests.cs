using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

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

	/// <summary>A line may name an alias an earlier line made; it is an alias of that alias's target, switches and all.</summary>
	[Test]
	public async Task AnAliasOfAnEarlierAliasNamesItsTarget()
	{
		var resolved = Aliases.Resolve(Import(
			Command("@qf2", "@force/inplace"),
			Command("@qf3", "@qf2"),
			Function("plusser", "add"),
			Function("plusser2", "plusser")), ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Commands["@FORCE/INPLACE"]).IsEquivalentTo(new[] { "@qf2", "@qf3" });
		await Assert.That(resolved.Functions["add"]).IsEquivalentTo(new[] { "plusser", "plusser2" });
		await Assert.That(resolved.NotCarried).IsEmpty();
	}

	/// <summary>
	/// A command the running configuration disables is out of the live table, but an import that names it, to alias it
	/// or to restrict it, still finds it: the file being applied may be the one that re-enables it.
	/// </summary>
	[Test]
	public async Task ACommandTheGameHasDisabledIsStillFound()
	{
		var live = WebAppFactoryArg.Services.GetRequiredService<ILibraryProvider<CommandDefinition>>();
		var emit = live.Get()["@EMIT"].LibraryInformation.Attribute;
		var parked = new CommandLibraryService();
		foreach (var (name, entry) in live.Get().Where(entry => !ReferenceEquals(entry.Value.LibraryInformation.Attribute, emit)))
		{
			parked.Add(name, entry);
		}

		var commands = Substitute.For<ILibraryProvider<CommandDefinition>>();
		commands.Get().Returns(parked);
		commands.Builtins.Returns(live.Builtins);
		var aliases = new ConfiguredAliasImport(commands, WebAppFactoryArg.Services.GetRequiredService<ILibraryProvider<FunctionDefinition>>());

		var import = Import(Command("@em2", "@emit")) with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(new Dictionary<string, string[]> { ["@emit"] = ["noguest"] },
					new Dictionary<string, string[]>())
			}
		};
		var resolved = aliases.Resolve(import, ConfigurationDialect.PennMUSH);

		await Assert.That(resolved.Commands["@EMIT"]).IsEquivalentTo(new[] { "@em2" });
		await Assert.That(resolved.CommandRestrictions.Keys).IsEquivalentTo(new[] { "@emit" });
		await Assert.That(resolved.NotCarried).IsEmpty();
	}

	/// <summary>An alias that is also an abbreviation is kept, so a restriction naming it finds the exact name.</summary>
	[Test]
	public async Task AnAliasThatAbbreviatesItsCommandIsKept()
	{
		var import = Import(Command("@emi", "@emit")) with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(new Dictionary<string, string[]> { ["@emi"] = ["nobody"] },
					new Dictionary<string, string[]>())
			}
		};

		var resolved = Aliases.Resolve(import, ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Commands["@EMIT"]).IsEquivalentTo(new[] { "@emi" });
		await Assert.That(resolved.CommandRestrictions.Keys).IsEquivalentTo(new[] { "@emi" });
		await Assert.That(resolved.NotCarried).IsEmpty();
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

	/// <summary>
	/// An alias an earlier import added is in the live table by now, but the options written by this import start
	/// from SharpMUSH's own, so it is resolved again; only a shipped alias is left out. The table takes an alias the
	/// configuration gains at once, and a restriction on a function alias made by the same file is not reported.
	/// </summary>
	[Test]
	public async Task AnAliasAnEarlierImportAddedIsKept()
	{
		var suffix = Guid.NewGuid().ToString("N")[..8];
		var commandAlias = $"@swk{suffix}";
		var functionAlias = $"plusk{suffix}";
		var registry = (IConfiguredAliasRegistry)WebAppFactoryArg.Services.GetRequiredService<ILibraryProvider<CommandDefinition>>();
		var functionRegistry = (IConfiguredAliasRegistry)WebAppFactoryArg.Services.GetRequiredService<ILibraryProvider<FunctionDefinition>>();
		var added = new AliasOptions(
			new Dictionary<string, string[]> { ["add"] = [functionAlias] },
			new Dictionary<string, string[]> { ["@SWITCH"] = [commandAlias] });

		await Assert.That(registry.RegisterConfiguredAliases(added)).IsTrue();
		await Assert.That(functionRegistry.RegisterConfiguredAliases(added)).IsTrue();
		await Assert.That(registry.RegisterConfiguredAliases(added)).IsFalse()
			.Because("an alias already there for the same command is left alone");

		var import = Import(Command(commandAlias, "@switch"), Function(functionAlias, "add")) with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(new Dictionary<string, string[]>(),
					new Dictionary<string, string[]> { [functionAlias] = ["wizard"] })
			}
		};
		var resolved = Aliases.Resolve(import, ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Commands["@SWITCH"]).IsEquivalentTo(new[] { commandAlias });
		await Assert.That(resolved.Functions["add"]).IsEquivalentTo(new[] { functionAlias });
		await Assert.That(resolved.NotCarried).IsEmpty();
	}

	[Test]
	public async Task AFunctionRestrictionOnANewAliasIsNotReported()
	{
		var alias = $"plusn{Guid.NewGuid().ToString("N")[..8]}";
		var import = Import(Function(alias, "add")) with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(new Dictionary<string, string[]>(),
					new Dictionary<string, string[]> { [alias] = ["wizard"] })
			}
		};

		var resolved = Aliases.Resolve(import, ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.Functions["add"]).IsEquivalentTo(new[] { alias });
		await Assert.That(resolved.NotCarried).IsEmpty();
	}

	/// <summary>
	/// A restriction on a name the dialect gives a built-in is kept under the built-in's name, since a call by the
	/// dialect's name runs that built-in and is held to its restriction.
	/// </summary>
	[Test]
	public async Task ARestrictionOnADialectNameIsKeptUnderTheBuiltInsName()
	{
		var import = Import() with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(new Dictionary<string, string[]>(),
					new Dictionary<string, string[]> { ["secs2time"] = ["nobody"] })
			}
		};

		var resolved = Aliases.Resolve(import, ConfigurationDialect.TinyMUX);

		await Assert.That(resolved.FunctionRestrictions.Keys.Single()).IsEqualTo("convsecs").Because("TinyMUX's secs2time is convsecs");
		await Assert.That(resolved.NotCarried).IsEmpty();
	}

	/// <summary>Restrictions on two dialect names for one built-in add up rather than the first winning.</summary>
	[Test]
	public async Task RestrictionsOnNamesForTheSameBuiltInAddUp()
	{
		var import = Import() with
		{
			Options = SharpMUSHOptions.Default() with
			{
				Restriction = new RestrictionOptions(new Dictionary<string, string[]>(),
					new Dictionary<string, string[]> { ["totmatch"] = ["wizard"], ["totwildmatch"] = ["nobody"] })
			}
		};

		var resolved = Aliases.Resolve(import, ConfigurationDialect.RhostMUSH);

		await Assert.That(resolved.FunctionRestrictions.Keys.Single()).IsEqualTo("matchall");
		await Assert.That(resolved.FunctionRestrictions["matchall"]).IsEquivalentTo(new[] { "wizard", "nobody" });
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
