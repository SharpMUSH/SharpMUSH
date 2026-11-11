using System.Text.RegularExpressions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// The compatibility options are feature flags: each says which servers it emulates, which database imports set
/// it and which help section documents it, and these tests keep the importers and the help in step with that.
/// </summary>
public class CompatibilityFlagTests
{
	private static readonly string[] Groups =
		["PennMUSH Compatibility", "TinyMUSH Compatibility", "TinyMUX Compatibility", "RhostMUSH Compatibility", "Dialects"];

	private static IEnumerable<(string Property, SharpConfigAttribute Option)> Flags()
		=> ConfigMetadata.PropertyNames
			.Where(property => ConfigAccessor.GetCategoryForProperty(property) == nameof(SharpMUSHOptions.Compatibility))
			.Select(property => (property, ConfigMetadata.PropertyMetadata[property]));

	private static bool IsBoolean(string property) => ConfigAccessor.GetPropertyType(property) == typeof(bool);

	private static string Helpfile(string name)
		=> File.ReadAllText(Path.Combine(TestPaths.Helpfiles.FullName, name));

	private static HelpArticle CompatibilityConfig()
		=> HelpArticleParser.Parse(Helpfile("compatibility-config.md"), "help").Single().Article;

	[Test]
	public async ValueTask EveryFlagSaysWhatItEmulatesAndWhereItIsDocumented()
	{
		var flags = Flags().ToList();
		await Assert.That(flags.Count).IsGreaterThanOrEqualTo(27);
		foreach (var (_, option) in flags)
		{
			await Assert.That(option.Emulates).IsNotEqualTo(CompatibilityServer.None).Because(option.Name);
			await Assert.That(option.HelpTopic).IsNotNull().Because(option.Name);
			await Assert.That(Groups).Contains(option.Group!).Because(option.Name);
		}
	}

	/// <summary>A flag is grouped under the server it comes from, which is one it emulates.</summary>
	[Test]
	public async ValueTask EveryFlagIsGroupedUnderAServerItEmulates()
	{
		foreach (var (_, option) in Flags().Where(flag => flag.Option.Group != "Dialects"))
		{
			var server = Enum.Parse<CompatibilityServer>(option.Group!.Split(' ')[0]);
			await Assert.That(option.Emulates.HasFlag(server)).IsTrue().Because(option.Name);
		}
	}

	[Test]
	public async ValueTask EveryHelpTopicIsASectionThatAnswersToTheOptionsName()
	{
		var sections = CompatibilityConfig().Sections;
		foreach (var (_, option) in Flags())
		{
			var section = sections.SingleOrDefault(section => section.Lookup == option.HelpTopic);
			await Assert.That(section).IsNotNull().Because($"{option.Name}: {option.HelpTopic}");
			await Assert.That(section!.Aliases).Contains(option.Name);
			await Assert.That(section.Heading).Contains($"`{option.Name}`");
		}
	}

	/// <summary>Each section says which imports set its options, or that none does.</summary>
	[Test]
	public async ValueTask EveryHelpSectionNamesTheImportsThatSetIt()
	{
		var sections = CompatibilityConfig().Sections;
		foreach (var (_, option) in Flags())
		{
			var text = sections.Single(section => section.Lookup == option.HelpTopic).Markdown;
			if (option.SetBy == CompatibilityServer.None)
			{
				await Assert.That(text).Contains("No database import").Because(option.Name);
				continue;
			}

			await Assert.That(text).Contains("import").Because(option.Name);
			foreach (var server in CompatibilityServers.Names(option.SetBy))
			{
				await Assert.That(text).Contains(server).Because($"{option.Name} is set by a {server} import");
			}
		}
	}

	/// <summary>
	/// <c>@config tiny</c> lists every flag once, and its line ends with the sentence <c>@config</c> and the
	/// configuration page make from <see cref="SharpConfigAttribute.SetBy"/>.
	/// </summary>
	[Test]
	public async ValueTask ConfigTinyHelpListsEveryFlagWithItsImports()
	{
		var help = Helpfile("config-tiny-command.md");
		foreach (var (property, option) in Flags())
		{
			var lines = Regex.Matches(help, $@"^- `{option.Name}=.*$", RegexOptions.Multiline);
			await Assert.That(lines.Count).IsEqualTo(1).Because(option.Name);
			var sentence = CompatibilityServers.ImportSentence(option, IsBoolean(property));
			if (sentence is null)
			{
				await Assert.That(lines[0].Value).DoesNotContain("database turns it on").Because(option.Name);
			}
			else
			{
				await Assert.That(lines[0].Value).EndsWith(sentence).Because(option.Name);
			}
		}
	}

	[Test]
	[Arguments(CompatibilityServer.PennMUSH)]
	[Arguments(CompatibilityServer.TinyMUX)]
	[Arguments(CompatibilityServer.RhostMUSH)]
	public async ValueTask AnImportSetsExactlyTheFlagsMarkedForIt(CompatibilityServer server)
	{
		var marked = Flags().Where(flag => flag.Option.SetBy.HasFlag(server)).Select(flag => flag.Option.Name).ToHashSet();
		var options = server switch
		{
			CompatibilityServer.PennMUSH => new PennMUSHDatabase { Version = "" }.SourceOptions,
			CompatibilityServer.TinyMUX => TinyMuxDatabaseReader.CompatibilityOptions(),
			_ => RhostDatabaseTranslator.CompatibilityOptions()
		};
		var compatibility = Flags().Select(flag => flag.Option.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

		await Assert.That(options.Keys.Where(compatibility.Contains).Order()).IsEquivalentTo(marked.Order());
		foreach (var name in marked.Where(name => name != "dialect"))
		{
			await Assert.That(options[name] is true).IsTrue();
		}
	}

	/// <summary>The flags each import set before the importers read them from the metadata.</summary>
	[Test]
	public async ValueTask TheImportsSetTheFlagsTheyAlwaysHave()
	{
		await Assert.That(new PennMUSHDatabase { Version = "" }.SourceOptions.Keys.Order())
			.IsEquivalentTo(new[] { "http_qregisters", "mapsql_sync", "paren_groups" });
		await Assert.That(TinyMuxDatabaseReader.CompatibilityOptions().Keys.Order()).IsEquivalentTo(new[]
		{
			"attribute_eval_flags", "dialect", "function_recursion_limit", "innermost_tokens", "mux_arguments",
			"mux_collation", "mux_function_caller", "mux_justify", "mux_logic", "mux_queue_functions", "mux_quiet",
			"null_eq_zero", "paren_groups", "protection_flags", "rhost_list", "tiny_math", "tiny_trim_fun"
		});
		await Assert.That(RhostDatabaseTranslator.CompatibilityOptions().Keys.Order()).IsEquivalentTo(new[]
		{
			"attribute_eval_flags", "command_aliases", "dialect", "mux_logic", "paren_groups", "protection_flags",
			"rhost_edit", "rhost_lattr", "rhost_list", "rhost_lrooms", "rhost_map_args", "rhost_printf", "sidefx_flag",
			"tiny_trim_fun"
		});
		await Assert.That(TinyMuxDatabaseReader.CompatibilityOptions()["dialect"]).IsEqualTo("mux");
		await Assert.That(RhostDatabaseTranslator.CompatibilityOptions()["dialect"]).IsEqualTo("rhost");
	}

	[Test]
	public async ValueTask TheConfigurationPageSaysWhatAFlagEmulatesAndWhichImportsSetIt()
	{
		var option = ConfigMetadata.PropertyMetadata[nameof(CompatibilityOptions.MuxLogic)];

		await Assert.That(CompatibilityServers.Describe(option, isBoolean: true)).EndsWith(
			". Emulates TinyMUX and RhostMUSH. Importing a TinyMUX or RhostMUSH database turns it on.");
		await Assert.That(CompatibilityServers.Describe(ConfigMetadata.PropertyMetadata[nameof(CompatibilityOptions.Dialect)], isBoolean: false))
			.EndsWith("Importing a TinyMUX or RhostMUSH database sets it.");
		await Assert.That(CompatibilityServers.Describe(ConfigMetadata.PropertyMetadata[nameof(CompatibilityOptions.TinyBooleans)], isBoolean: true))
			.EndsWith("Emulates TinyMUSH.");
	}

	[Test]
	public async ValueTask TheImportReportNamesEachChangeAndItsHelp()
	{
		await Assert.That(PennMUSHDatabaseConverter.OptionChange("TinyMUX", "mux_logic", nameof(CompatibilityOptions.MuxLogic), false, true))
			.IsEqualTo("The TinyMUX import turned mux_logic on. See help compatibility config logical functions mux logic.");
		await Assert.That(PennMUSHDatabaseConverter.OptionChange("TinyMUX", "dialect", nameof(CompatibilityOptions.Dialect), null, "mux"))
			.IsEqualTo("The TinyMUX import set dialect to mux (it was empty). See help compatibility config other servers names dialect.");
		await Assert.That(PennMUSHDatabaseConverter.OptionChange("TinyMUX", "function_recursion_limit", nameof(LimitOptions.FunctionRecursionLimit), 100u, 500u))
			.IsEqualTo("The TinyMUX import set function_recursion_limit to 500 (it was 100).");
	}
}
