using System.Text.Json;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Holds the PennMUSH compatibility profile (<c>pennmush-compatibility.md</c>, #1134) to its own
/// format, so a reader can tell a deliberate difference from an open question or a bug by where it is
/// and what it says. <see cref="CompatibilityProfileExampleTests"/> runs its examples; this checks the
/// entries around them.
///
/// <para>An entry is a <c>## </c> heading and the text up to the next heading. One that describes a
/// difference has a <c>**PennMUSH**</c> paragraph. Outside [COMPATIBILITY UNRESOLVED] such an entry
/// is a decision already made, so it says <c>**A choice.**</c> and gives what SharpMUSH does, why, a
/// workaround and an example. An entry that cannot be shown from a single session says
/// <c>**No example.**</c> and why, so the gap is visible rather than silent.</para>
///
/// <para>The differential harness's allowlist, <c>tools/parity/known-differences.json</c>, may only
/// excuse a difference the profile records as a choice: a difference that is not written up there is
/// a bug for the harness's baseline, not an allowed one.</para>
/// </summary>
public class CompatibilityProfileStructureTests
{
	private const string Profile = "pennmush-compatibility.md";
	private const string Choice = "**A choice.**";
	private const string Unresolved = "COMPATIBILITY UNRESOLVED";

	/// <summary>One <c>## </c> entry: its section (the <c>#</c> heading above it), heading and text.</summary>
	public sealed record ProfileEntry(int Line, string Section, string Heading, string Body)
	{
		public bool DescribesADifference => Body.Contains("**PennMUSH**", StringComparison.Ordinal);
		public bool IsChoice => Body.Contains(Choice, StringComparison.Ordinal);

		public override string ToString() => $"{Profile}:{Line} {Heading}";
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

		var hasExample = entry.Body.Contains("```sharp", StringComparison.Ordinal)
			|| entry.Body.Contains("**No example.**", StringComparison.Ordinal);
		await Assert.That(hasExample).IsTrue()
			.Because($"{entry} needs a ```sharp example, or a **No example.** line saying why it has none");
	}

	[Test]
	public async ValueTask UnresolvedEntriesAreNotChoices()
	{
		var unresolved = ReadEntries().Where(entry => entry.Section == Unresolved).ToList();

		await Assert.That(unresolved).IsNotEmpty();
		foreach (var entry in unresolved)
		{
			await Assert.That(entry.IsChoice).IsFalse().Because($"{entry} has no decision yet");
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
		await Assert.That(DecidedDifferences().Count()).IsGreaterThan(20);
	}

	public static IEnumerable<Func<ProfileEntry>> DecidedDifferences() =>
		ReadEntries()
			.Where(entry => entry.Section != Unresolved && (entry.DescribesADifference || entry.IsChoice))
			.Select(entry => (Func<ProfileEntry>)(() => entry));

	private static IEnumerable<ProfileEntry> ReadEntries()
	{
		var lines = File.ReadAllLines(Path.Combine(TestPaths.Helpfiles.FullName, Profile));
		var section = string.Empty;
		var inFence = false;

		for (var index = 0; index < lines.Length; index++)
		{
			if (lines[index].StartsWith("```", StringComparison.Ordinal))
				inFence = !inFence;
			if (inFence)
				continue;

			if (lines[index].StartsWith("# ", StringComparison.Ordinal))
				section = lines[index][2..].Trim();
			if (!lines[index].StartsWith("## ", StringComparison.Ordinal))
				continue;

			var body = new List<string>();
			var bodyInFence = false;
			for (var next = index + 1; next < lines.Length; next++)
			{
				if (lines[next].StartsWith("```", StringComparison.Ordinal))
					bodyInFence = !bodyInFence;
				if (!bodyInFence && lines[next].StartsWith('#'))
					break;
				body.Add(lines[next]);
			}

			yield return new ProfileEntry(index + 1, section, lines[index][3..].Trim(), string.Join('\n', body));
		}
	}
}
