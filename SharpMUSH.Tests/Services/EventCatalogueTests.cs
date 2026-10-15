using System.Reflection;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The event catalogue is the one description of every event: each raise names a catalogued event, each event
/// the catalogue says is raised is raised somewhere, and help documents each one with its arguments.
/// </summary>
public partial class EventCatalogueTests
{
	/// <summary>
	/// A raise's event: a name (<c>SharpEvents.X</c>), or a switch expression choosing one (<c>kind switch { ... }</c>),
	/// whose arms are read one by one.
	/// </summary>
	[GeneratedRegex(@"TriggerEventAsync\(\s*(?:(?<switch>\w+ switch\s*\{[^}]*\})|(?<name>[^,\s)]+))")]
	private static partial Regex RaiseRegex();

	[GeneratedRegex(@"=>\s*([^,\s}]+)")]
	private static partial Regex SwitchArmRegex();

	[Test]
	public async Task EveryRaiseNamesACataloguedEventThroughSharpEvents()
	{
		var offenders = Raises()
			.Where(raise => raise.Event is not { } name || EventCatalogue.Find(name) is not { Support: EventSupport.Raised or EventSupport.Compatibility })
			.Select(raise => $"{raise.File}: {raise.Expression}")
			.ToArray();

		await Assert.That(offenders).IsEmpty()
			.Because("every raise names its event as a SharpEvents constant for an event the catalogue marks raised: "
				+ string.Join(", ", offenders));
	}

	[Test]
	public async Task EveryEventTheCatalogueMarksRaisedIsRaised()
	{
		var raised = Raises().Select(raise => raise.Event).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
		var neverRaised = EventCatalogue.All
			.Where(definition => definition.Support is EventSupport.Raised or EventSupport.Compatibility && !raised.Contains(definition.Name))
			.Select(definition => definition.Name)
			.ToArray();

		await Assert.That(neverRaised).IsEmpty()
			.Because("an event marked raised that nothing raises is documented as working while it is dead: "
				+ string.Join(", ", neverRaised));
	}

	[Test]
	public async Task NamesAreUpperCaseAndUnique()
	{
		await Assert.That(EventCatalogue.All.Select(definition => definition.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count())
			.IsEqualTo(EventCatalogue.All.Count);
		await Assert.That(EventCatalogue.All.Where(definition => definition.Name != definition.Name.ToUpperInvariant() || !definition.Name.Contains('`')).Select(definition => definition.Name).ToArray())
			.IsEmpty();
	}

	[Test]
	public async Task HelpListsEveryRaisedEventWithItsArguments()
	{
		var help = File.ReadAllText(HelpFile("sharpevents.md"))
			.Replace("\\`", "`");
		var missing = EventCatalogue.All
			.Where(definition => definition.Support is EventSupport.Raised or EventSupport.Compatibility)
			.Where(definition => !help.Contains(HelpSignature(definition), StringComparison.Ordinal))
			.Select(HelpSignature)
			.ToArray();

		await Assert.That(missing).IsEmpty()
			.Because("help event <group> documents each raised event as **name** (*argument*, ...): " + string.Join(", ", missing));
	}

	[Test]
	public async Task HelpSaysEachCompatibilityEventNeedsTheOption()
	{
		var help = File.ReadAllText(HelpFile("sharpevents.md"))
			.Replace("\\`", "`");
		var unmarked = EventCatalogue.All
			.Where(definition => definition.Support == EventSupport.Compatibility)
			.Where(definition => !HelpTopicAround(help, HelpSignature(definition)).Contains("compatibility_events", StringComparison.Ordinal))
			.Select(definition => definition.Name)
			.ToArray();

		await Assert.That(unmarked).IsEmpty()
			.Because("an event raised only under compatibility_events says so in its help topic: " + string.Join(", ", unmarked));
	}

	/// <summary>The help topic (from one <c># </c> heading to the next) that holds <paramref name="text"/>.</summary>
	private static string HelpTopicAround(string help, string text)
	{
		var at = help.IndexOf(text, StringComparison.Ordinal);
		if (at < 0)
		{
			return string.Empty;
		}

		var start = help.LastIndexOf("\n# ", at, StringComparison.Ordinal);
		var end = help.IndexOf("\n# ", at, StringComparison.Ordinal);
		return help[(start < 0 ? 0 : start)..(end < 0 ? help.Length : end)];
	}

	private static string HelpSignature(EventDefinition definition)
		=> definition.Arguments.Count == 0
			? $"**{definition.Name.ToLowerInvariant()}**"
			: $"**{definition.Name.ToLowerInvariant()}** ({string.Join(", ", definition.ArgumentNames.Select(name => $"*{name}*"))})";

	private sealed record Raise(string File, string Expression, string? Event);

	/// <summary>Every <c>TriggerEventAsync</c> call in the shipped projects, with the event it names when that is a <see cref="SharpEvents"/> constant.</summary>
	private static IEnumerable<Raise> Raises()
	{
		var constants = typeof(SharpEvents).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.IsLiteral)
			.ToDictionary(field => $"SharpEvents.{field.Name}", field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
		var root = RepositoryRoot();

		return Directory.EnumerateDirectories(root, "SharpMUSH.*")
			.Where(directory => !Path.GetFileName(directory).Contains("Tests", StringComparison.Ordinal))
			.SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
			.Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
				&& !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.SelectMany(file => RaiseRegex().Matches(File.ReadAllText(file))
				.SelectMany(match => match.Groups["switch"].Success
					? SwitchArmRegex().Matches(match.Groups["switch"].Value).Select(arm => arm.Groups[1].Value)
					: [match.Groups["name"].Value])
				// The declaration and the implementation take the name as a parameter.
				.Where(expression => expression is not ("string" or "eventName"))
				.Select(expression => new Raise(Path.GetRelativePath(root, file), expression, constants.GetValueOrDefault(expression))));
	}

	private static string HelpFile(string name)
		=> Path.Join(RepositoryRoot(), "SharpMUSH.Documentation", "Helpfiles", "SharpMUSH", name);

	/// <summary>Walks up from the test binaries to the directory holding <c>SharpMUSH.sln</c>.</summary>
	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Join(directory.FullName, "SharpMUSH.sln")))
			directory = directory.Parent;

		return directory?.FullName
			?? throw new InvalidOperationException("Could not locate the repository root (no SharpMUSH.sln above the test binaries).");
	}
}
