using System.Reflection;
using System.Text.RegularExpressions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// The compatibility options are feature flags: each says once, on its <see cref="CompatibilityFlagAttribute"/>,
/// which servers it emulates, which database imports turn it on and where it is documented. These tests keep the
/// code, the importers and the help in step with that one declaration.
/// </summary>
public partial class CompatibilityFlagTests
{
	private static readonly string[] Groups = ["PennMUSH", "TinyMUSH", "TinyMUX", "RhostMUSH", "Dialects"];

	private static IEnumerable<SharpConfigAttribute> CompatibilityOptionsDeclared()
		=> typeof(CompatibilityOptions).GetProperties()
			.Select(property => property.GetCustomAttribute<SharpConfigAttribute>())
			.OfType<SharpConfigAttribute>()
			.Where(config => config.Category == "Compatibility");

	[Test]
	public async Task EveryCompatibilityOptionIsAFlag()
	{
		var declared = CompatibilityOptionsDeclared().Select(config => config.Name).Order().ToList();
		await Assert.That(declared).IsNotEmpty();
		await Assert.That(CompatibilityFlags.All.Select(flag => flag.Name).Order().ToList()).IsEquivalentTo(declared);
	}

	[Test]
	public async Task EachFlagIsGroupedUnderAServerItEmulates()
	{
		var wrong = CompatibilityFlags.All
			.Where(flag => flag.Compatibility.Emulates == CompatibilityServer.None
				|| !Groups.Contains(flag.Config.Group)
				|| flag.Config.Group != "Dialects"
					&& !(Enum.TryParse<CompatibilityServer>(flag.Config.Group, out var origin) && flag.Compatibility.Emulates.HasFlag(origin)))
			.Select(flag => $"{flag.Name} ({flag.Config.Group}, emulates {flag.Compatibility.Emulates})")
			.ToList();
		await Assert.That(wrong).IsEmpty();
	}

	[Test]
	public async Task OnlyDatabaseImportsSetAFlag()
	{
		var wrong = CompatibilityFlags.All
			.Where(flag => flag.Compatibility.SetBy.HasFlag(CompatibilityServer.TinyMUSH))
			.Select(flag => flag.Name)
			.ToList();
		await Assert.That(wrong).IsEmpty().Because("there is no TinyMUSH database import");
	}

	[Test]
	public async Task EachFlagsHelpTopicIsACompatibilityConfigSectionNamingIt()
	{
		var sections = HelpSections("compatibility-config.md");
		var wrong = CompatibilityFlags.All
			.Where(flag => !sections.TryGetValue(flag.Compatibility.HelpTopic, out var heading) || !heading.Contains($"`{flag.Name}`"))
			.Select(flag => $"{flag.Name}: {flag.Compatibility.HelpTopic}")
			.ToList();
		await Assert.That(wrong).IsEmpty();
	}

	[Test]
	public async Task EachFlagIsListedInConfigHelp()
	{
		var help = File.ReadAllText(Path.Combine(TestPaths.Helpfiles.FullName, "config-tiny-command.md"));
		var missing = CompatibilityFlags.All
			.Where(flag => !help.Contains($"`{flag.Name}=", StringComparison.Ordinal))
			.Select(flag => flag.Name)
			.ToList();
		await Assert.That(missing).IsEmpty();
	}

	[Test]
	[Arguments(CompatibilityServer.PennMUSH)]
	[Arguments(CompatibilityServer.TinyMUX)]
	[Arguments(CompatibilityServer.RhostMUSH)]
	public async Task AnImportSetsExactlyTheFlagsMarkedForIt(CompatibilityServer source)
	{
		var imported = source switch
		{
			CompatibilityServer.PennMUSH => new PennMUSHDatabase { Version = "test" }.SourceOptions,
			CompatibilityServer.TinyMUX => TinyMuxDatabaseReader.CompatibilityOptions(),
			_ => RhostDatabaseTranslator.CompatibilityOptions()
		};
		var flagNames = CompatibilityFlags.All.Select(flag => flag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var set = imported.Keys.Where(flagNames.Contains).Order(StringComparer.OrdinalIgnoreCase).ToList();
		var marked = CompatibilityFlags.All
			.Where(flag => flag.Compatibility.SetBy.HasFlag(source))
			.Select(flag => flag.Name)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(set).IsEquivalentTo(marked);
		await Assert.That(imported.Where(entry => flagNames.Contains(entry.Key) && entry.Key != "dialect").All(entry => entry.Value is true)).IsTrue();
	}

	[Test]
	public async Task TheDialectImportsSetTheirOwnDialect()
	{
		await Assert.That(TinyMuxDatabaseReader.CompatibilityOptions()["dialect"]).IsEqualTo("mux");
		await Assert.That(RhostDatabaseTranslator.CompatibilityOptions()["dialect"]).IsEqualTo("rhost");
	}

	/// <summary>
	/// A description does not say which imports turn the option on; the metadata adds that sentence from
	/// <see cref="CompatibilityFlagAttribute.SetBy"/>, so the two cannot disagree.
	/// </summary>
	[Test]
	public async Task TheImportSentenceIsGeneratedFromSetBy()
	{
		var wrong = new List<string>();
		foreach (var flag in CompatibilityFlags.All)
		{
			if (ImportSentence().IsMatch(flag.Config.Description))
			{
				wrong.Add($"{flag.Name}: its description names an import by hand");
			}

			var generated = ConfigMetadata.PropertyMetadata[flag.Property.Name].Description;
			var said = ImportSentence().Match(generated);
			if (flag.Compatibility.SetBy == CompatibilityServer.None)
			{
				if (said.Success) wrong.Add($"{flag.Name}: no import sets it, but the metadata says one does");
				continue;
			}

			var named = flag.Compatibility.SetBy.ToString().Split(", ");
			if (!said.Success || named.Any(server => !said.Value.Contains(server)))
			{
				wrong.Add($"{flag.Name}: the metadata says '{(said.Success ? said.Value : generated)}', not {flag.Compatibility.SetBy}");
			}
		}

		await Assert.That(wrong).IsEmpty();
	}

	/// <summary>The <c>heading</c> of each section of a help file's metadata, by its <c>lookup</c>.</summary>
	private static Dictionary<string, string> HelpSections(string file)
		=> SectionMetadata().Matches(File.ReadAllText(Path.Combine(TestPaths.Helpfiles.FullName, file)))
			.ToDictionary(match => match.Groups["lookup"].Value, match => match.Groups["heading"].Value, StringComparer.OrdinalIgnoreCase);

	[GeneratedRegex(@"""heading"":\s*""(?<heading>[^""]*)"",\s*""lookup"":\s*""(?<lookup>[^""]*)""")]
	private static partial Regex SectionMetadata();

	[GeneratedRegex(@"A [A-Za-z, ]+ database import (turns it on|sets it)|Importing an? [A-Za-z ]+ database")]
	private static partial Regex ImportSentence();
}
