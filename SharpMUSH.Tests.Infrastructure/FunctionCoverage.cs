using System.Text;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests;

/// <summary>
/// Which registered functions the suite mentions, and which it actually runs.
///
/// <para>The two are not the same. A test source containing <c>vadd(</c> may only be asserting on an
/// error message, or listing names in a comment; the function never goes through the parser. The
/// mention scan is cheap and works on any subset of the suite, so it is the gate that runs everywhere.
/// The executed set comes from the parser's own <c>sharpmush.function.invocation.duration</c>
/// measurements, observed by <see cref="ServerWebAppFactory"/>'s meter listener, so it is only
/// meaningful after a whole-suite run. The gap between the two — mentioned but never dispatched — is
/// what the telemetry report lists.</para>
/// </summary>
public static class FunctionCoverage
{
	/// <summary>How one run's executed set compares with the registry and the test sources.</summary>
	/// <param name="Registered">Every registered function name.</param>
	/// <param name="Executed">Registered names the parser dispatched at least once, by name or alias.</param>
	/// <param name="MentionedOnly">Named as <c>name(</c> in a test source, but never dispatched.</param>
	/// <param name="NeverMentioned">Not named anywhere in the test sources.</param>
	public sealed record Report(
		IReadOnlyList<string> Registered,
		IReadOnlyList<string> Executed,
		IReadOnlyList<string> MentionedOnly,
		IReadOnlyList<string> NeverMentioned);

	private static readonly Lazy<string> TestCorpus = new(() =>
		string.Join('\n', TestPaths.TestSourceFiles()
			.Concat(Directory.EnumerateFiles(
				Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Tests"), "*.t", SearchOption.AllDirectories))
			.Select(File.ReadAllText))
		.ToLowerInvariant());

	private static readonly Lazy<IReadOnlyList<string>> LazyRegistered = new(() =>
		RegistryInventory.Functions
			.Select(f => f.Name.ToLowerInvariant())
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToList());

	/// <summary>
	/// Registered name for every name a caller can type. The parser measures the name as typed, so a
	/// test that only ever calls <c>exp()</c> has still run <c>e()</c>.
	/// </summary>
	private static readonly Lazy<IReadOnlyDictionary<string, string>> LazyCanonical = new(() =>
	{
		var canonical = LazyRegistered.Value.ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);
		foreach (var (name, aliases) in AliasOptions.Default.FunctionAliases)
			foreach (var alias in aliases)
				canonical.TryAdd(alias, name.ToLowerInvariant());
		return canonical;
	});

	/// <summary>Every registered function name, lower-cased and sorted.</summary>
	public static IReadOnlyList<string> Registered => LazyRegistered.Value;

	/// <summary>Registered names that no test source spells as <c>name(</c>.</summary>
	public static IReadOnlyList<string> NeverMentioned() =>
		Registered.Where(name => !IsMentioned(name)).ToList();

	/// <summary>
	/// Classifies <paramref name="dispatchedNames"/> — the <c>function.name</c> tags the parser
	/// measured — against the registry. Names the registry does not know (prose that looked like a call,
	/// <c>@function</c> globals) are dropped.
	/// </summary>
	public static Report Build(IEnumerable<string> dispatchedNames)
	{
		var executed = dispatchedNames
			.Select(n => LazyCanonical.Value.GetValueOrDefault(n))
			.OfType<string>()
			.ToHashSet(StringComparer.Ordinal);

		var notExecuted = Registered.Where(n => !executed.Contains(n)).ToList();
		return new Report(
			Registered,
			Registered.Where(executed.Contains).ToList(),
			notExecuted.Where(IsMentioned).ToList(),
			notExecuted.Where(n => !IsMentioned(n)).ToList());
	}

	/// <summary>The report as a markdown section for the CI step summary.</summary>
	public static string ToMarkdown(Report report)
	{
		var sb = new StringBuilder();
		sb.AppendLine("### 🧪 Function Coverage");
		sb.AppendLine();
		sb.AppendLine($"{report.Executed.Count} of {report.Registered.Count} registered functions were dispatched by the parser during this run.");
		sb.AppendLine();
		AppendList(sb, "Mentioned in a test source but never dispatched", report.MentionedOnly);
		AppendList(sb, "Not mentioned in any test source", report.NeverMentioned);
		return sb.ToString();
	}

	private static void AppendList(StringBuilder sb, string heading, IReadOnlyList<string> names)
	{
		sb.AppendLine($"**{heading} ({names.Count}):** {(names.Count == 0 ? "none" : string.Join(", ", names.Select(n => $"`{n}()`")))}");
		sb.AppendLine();
	}

	private static bool IsMentioned(string name) =>
		TestCorpus.Value.Contains($"{name}(", StringComparison.Ordinal);
}
