using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Holds the PennMUSH compatibility profile (<c>pennmush-compatibility.md</c>, #1134) to its own
/// format, so a reader can tell a deliberate difference from an open question or a bug by where it is
/// and what it says. <see cref="CompatibilityProfileExampleTests"/> runs its examples; this checks the
/// entries around them.
///
/// <para>An entry is a <c>## </c> heading and the text up to the next heading. Every entry is a
/// decided difference unless its section says otherwise ([COMPATIBILITY UNRESOLVED], [COMPATIBILITY
/// DEFECTS], [COMPATIBILITY MATCHED]) or <see cref="NotADifference"/> names it with the reason. A
/// decided difference says <c>**A choice.**</c> and gives what PennMUSH does, what SharpMUSH does, why,
/// a workaround and an example. The example is a <c>```sharp</c> block, or, when it needs a second
/// player, God or a server setting, an <c>**Example.**</c> line naming the parity harness case that runs
/// it on both servers. Only the entries in <see cref="NoExample"/> may say <c>**No example.**</c>
/// instead, each with the reason there is none.</para>
///
/// <para>The differential harness's allowlist, <c>tools/parity/known-differences.json</c>, may only
/// excuse a difference the profile records as a choice: a difference that is not written up there is
/// a bug for the harness's baseline, not an allowed one.</para>
/// </summary>
public class CompatibilityProfileStructureTests
{
	private const string Profile = "pennmush-compatibility.md";
	private const string Choice = "**A choice.**";
	private const string Unresolved = "Compatibility Unresolved";

	/// <summary>Sections whose entries are, by definition, not decisions.</summary>
	private static readonly HashSet<string> UndecidedSections =
		[Unresolved, "Compatibility Defects", "Compatibility Matched"];

	/// <summary>
	/// Entries in a decided section that are not themselves a difference, and why. Anything else there
	/// has to be written up as a choice, so a new entry cannot skip the format by leaving out a marker.
	/// </summary>
	private static readonly Dictionary<string, string> NotADifference = new()
	{
		["Boolean compatibility: `tiny_booleans`"] = "describes a setting PennMUSH also has",
		["Numeric compatibility: `tiny_math`, `null_eq_zero`"] = "describes settings PennMUSH also has",
		["Trim argument order: `tiny_trim_fun`"] = "describes a setting PennMUSH also has",
		["`ansi()` and `lit()`"] = "only the declared maximum differs; behaviour matches",
		["Time precision"] = "the consequence of the choice *A trailing `<precision>` on the time functions*",
		["Objids and stamped dbrefs"] = "the representation decision is tracked in #1006 item 13",
		["Number precision"] = "describes `float_precision`, which matches PennMUSH",
		["SharpMUSH-only functions"] = "additions: PennMUSH softcode cannot call a name it never had",
		["SharpMUSH-only commands"] = "additions: PennMUSH softcode cannot call a name it never had",
	};

	/// <summary>
	/// Decided differences that no example can show, and why. Anything else needs one, so a new entry
	/// cannot leave the example out by writing <c>**No example.**</c>.
	/// </summary>
	private static readonly Dictionary<string, string> NoExample = new()
	{
		["A command that crashes says so"] = "a crash is a defect, fixed once found, so none is kept to show it",
		["`@config/set` is stored"] = "shows only after a restart; the parity harness starts each server once",
		["A `MAILFILTER` that mails its owner does not recurse"] = "PennMUSH's side crashes the server",
	};

	/// <summary>An <c>**Example.**</c> line: the parity case and the scenario file it is in.</summary>
	private static readonly Regex ParityExample = new(
		@"\*\*Example\.\*\* The parity case `(?<case>[^`]+)` in `tools/parity/scenarios/(?<scenario>[A-Za-z0-9._-]+)\.scn`",
		RegexOptions.Compiled);

	/// <summary>One <c>## </c> entry: its section (the <c>#</c> heading above it), heading and text.</summary>
	public sealed record ProfileEntry(int Line, string Section, string Heading, string Body, string Source = Profile)
	{
		public bool IsChoice => Body.Contains(Choice, StringComparison.Ordinal);

		public override string ToString() => $"{Source}:{Line} {Heading}";
	}

	[Test]
	[MethodDataSource(nameof(DecidedDifferences))]
	public async ValueTask EveryDecidedDifferenceIsAChoiceWithItsParts(ProfileEntry entry)
	{
		string[] parts = [Choice, "**PennMUSH**", "**SharpMUSH**", "**Why.**", "**Workaround.**"];

		foreach (var part in parts)
		{
			await Assert.That(entry.Body).Contains(part).Because($"{entry} is a decided difference");
		}

		var hasExample = entry.Body.Contains("```sharp", StringComparison.Ordinal) || ParityExample.IsMatch(entry.Body);
		if (NoExample.ContainsKey(entry.Heading))
		{
			await Assert.That(hasExample).IsFalse().Because($"{entry} has an example; take it out of NoExample");
			await Assert.That(entry.Body).Contains("**No example.**").Because($"{entry} is in NoExample and must say why");
			return;
		}

		await Assert.That(hasExample).IsTrue()
			.Because($"{entry} needs a ```sharp example or an **Example.** line naming its parity case");
		await Assert.That(entry.Body).DoesNotContain("**No example.**")
			.Because($"{entry} is not in NoExample, so it may not go without an example");
	}

	/// <summary>
	/// A parity case only shows a difference if the harness runs it and allowlists the step that differs
	/// under this entry. A case that is missing, or a difference excused under another heading, would
	/// leave the entry pointing at nothing.
	/// </summary>
	[Test]
	public async ValueTask ParityExamplesAreAllowlistedUnderTheirEntry()
	{
		var scenarios = Path.Combine(TestPaths.RepositoryRoot, "tools", "parity", "scenarios");
		using var allowlist = JsonDocument.Parse(
			File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tools", "parity", "known-differences.json")));
		var allowed = allowlist.RootElement.GetProperty("entries").EnumerateArray()
			.Select(known => (
				Scenario: known.GetProperty("scenario").GetString(),
				Case: known.GetProperty("case").GetString(),
				Profile: known.GetProperty("profile").GetString()))
			.ToList();

		var examples = ReadEntries()
			.SelectMany(entry => ParityExample.Matches(entry.Body).Select(match => (entry, match)))
			.ToList();
		await Assert.That(examples).IsNotEmpty();

		foreach (var (entry, match) in examples)
		{
			var scenario = match.Groups["scenario"].Value;
			var caseId = match.Groups["case"].Value;
			var file = Path.Combine(scenarios, scenario + ".scn");

			await Assert.That(File.Exists(file)).IsTrue().Because($"{entry} names {scenario}.scn");
			var declared = File.ReadLines(file).Any(line => line.StartsWith($"::case {caseId} ", StringComparison.Ordinal));
			await Assert.That(declared).IsTrue().Because($"{entry} names case {caseId}, which {scenario}.scn does not declare");
			await Assert.That(allowed).Contains((scenario, caseId, entry.Heading))
				.Because($"{entry}: known-differences.json must allowlist a step of {caseId} under this heading");
		}
	}

	/// <summary>An exception whose entry has gone would otherwise sit in the list unnoticed.</summary>
	[Test]
	public async ValueTask EveryNoExampleNamesADecidedDifference()
	{
		var decided = DecidedDifferences().Select(entry => entry().Heading).ToHashSet();

		foreach (var heading in NoExample.Keys)
		{
			await Assert.That(decided).Contains(heading).Because($"NoExample names \"{heading}\"");
		}
	}

	[Test]
	public async ValueTask UndecidedEntriesAreNotChoices()
	{
		var entries = ReadEntries().ToList();

		await Assert.That(entries.Count(entry => entry.Section == Unresolved)).IsGreaterThan(0);
		foreach (var entry in entries.Where(entry => UndecidedSections.Contains(entry.Section) || NotADifference.ContainsKey(entry.Heading)))
		{
			await Assert.That(entry.IsChoice).IsFalse().Because($"{entry} is not listed as a decided difference");
		}
	}

	/// <summary>An exclusion whose heading has gone would otherwise sit in the list unnoticed.</summary>
	[Test]
	public async ValueTask EveryExclusionNamesAnEntry()
	{
		var headings = ReadEntries().Select(entry => entry.Heading).ToHashSet();

		foreach (var heading in NotADifference.Keys)
		{
			await Assert.That(headings).Contains(heading).Because($"NotADifference names \"{heading}\"");
		}
	}

	/// <summary>
	/// The harness checks an allowlist entry's heading exists when it runs; this also checks, on every
	/// test run and without a PennMUSH build, that the heading is a choice and not an open question.
	/// </summary>
	[Test]
	public async ValueTask KnownDifferencesAreChoicesInTheProfile()
	{
		var choices = ReadEntries().Where(entry => entry.IsChoice).Select(entry => entry.Heading).ToHashSet();

		using var allowlist = JsonDocument.Parse(
			File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tools", "parity", "known-differences.json")));
		var entries = allowlist.RootElement.GetProperty("entries").EnumerateArray().ToList();

		await Assert.That(entries).IsNotEmpty();
		foreach (var known in entries)
		{
			var id = known.GetProperty("id").GetString();
			var heading = known.GetProperty("profile").GetString();
			await Assert.That(choices).Contains(heading!)
				.Because($"known-differences.json {id} excuses a difference the profile does not record as a choice");
		}
	}

	/// <summary>A heading parser regression would otherwise make the data-driven test pass vacuously.</summary>
	[Test]
	public async ValueTask ProfileHasDecidedDifferences()
	{
		await Assert.That(DecidedDifferences().Count()).IsGreaterThan(30);
		await Assert.That(DecidedDifferences().Select(entry => entry().Heading))
			.Contains("`render()` markup compatibility");
	}

	public static IEnumerable<Func<ProfileEntry>> DecidedDifferences() =>
		ReadEntries()
			.Where(entry => !UndecidedSections.Contains(entry.Section) && !NotADifference.ContainsKey(entry.Heading))
			.Select(entry => (Func<ProfileEntry>)(() => entry));

	private static IEnumerable<ProfileEntry> ReadEntries()
	{
		foreach (var source in CompatibilityProfileSources.All)
		{
			foreach (var section in source.Article.Sections)
			{
				var start = source.Markdown.IndexOf(section.Markdown, StringComparison.Ordinal);
				var line = source.Markdown[..start].Count(character => character == '\n') + 1;
				yield return new ProfileEntry(line, source.Article.Title, section.Heading, section.Markdown, source.FileName);
			}
		}
	}
}
