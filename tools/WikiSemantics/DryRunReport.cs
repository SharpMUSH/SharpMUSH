using System.Text;
using System.Text.Json;

namespace SharpMUSH.Tools.WikiSemantics;

public sealed record DryRunSuccess(int Pages, int Facts);
public union DryRunResult(DryRunSuccess, DatasetFailure);
public sealed record PropertyCoverage(string Property, int Subjects, int Values, int MissingMainSubjects);

public static class DryRunReport
{
	private static readonly UTF8Encoding Utf8 = new(false);
	public static DryRunResult Run(string source, string output, CancellationToken cancellationToken = default)
	{
		if (Directory.Exists(output) || File.Exists(output)) return new DatasetFailure("Output already exists.");
		string? stage = null;
		try
		{
			var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var destination = Path.GetFullPath(output);
			if (SymbolicAncestor(destination)) return new DatasetFailure("Symbolic output ancestors are unsupported.");
			if ((destination + Path.DirectorySeparatorChar).StartsWith(sourceRoot, StringComparison.Ordinal))
				return new DatasetFailure("Output must be outside the immutable source directory.");
			var verified = PinnedDataset.Load(source, cancellationToken);
			if (verified is DatasetFailure sourceFailure) return sourceFailure;
			if (verified is not VerifiedDataset dataset) throw new InvalidOperationException("Invalid dataset result.");
			var extraction = Pf2eExtractor.Extract(dataset, cancellationToken);
			if (extraction is DatasetFailure extractionFailure) return extractionFailure;
			if (extraction is not ExtractionSuccess extracted) throw new InvalidOperationException("Invalid extraction result.");
			var snapshot = extracted.Snapshot;
			var facts = snapshot.Facts.Facts;
			var coverage = snapshot.Schema.Properties.Select(p =>
			{
				var values = facts.Where(f => f.Property == p.Id).ToArray();
				var subjects = values.Select(f => f.Subject).Distinct().Count();
				return new PropertyCoverage(p.Id, subjects, values.Length, 4580 - subjects);
			}).OrderBy(p => p.Property, StringComparer.Ordinal).ToArray();
			var expected = new Dictionary<string, (int Subjects, int Values)>(StringComparer.Ordinal)
			{
				["pf2e:level"] = (3396, 3396), ["pf2e:rank"] = (659, 659),
				["pf2e:tradition"] = (408, 894), ["pf2e:class"] = (904, 977),
				["pf2e:ancestry"] = (99, 99), ["pf2e:source-book"] = (4580, 4580)
			};
			var alchemist = snapshot.Pages.Single(p => p.Key.Namespace == "main" && p.Key.SourceKey == "alchemist").Reference;
			if (coverage.Any(c => expected[c.Property] != (c.Subjects, c.Values))
				|| snapshot.Pages.Count(p => p.Key.Namespace == "main") != 4580
				|| snapshot.Pages.Count(p => p.Key.Namespace == "category") != 130
				|| snapshot.Pages.Count(p => p.Key.Namespace == "help") != 1
				|| facts.Count(f => f.Property == "pf2e:class" && f.Value is PageRef target && target == alchemist) != 47
				|| extracted.Unknowns.Count(u => u.Property == "pf2e:ancestry") != 7)
				return new DatasetFailure("Pinned extraction coverage differs from independently reviewed expectations.");
			var parent = Path.GetDirectoryName(destination)!;
			if (SymbolicAncestor(destination)) return new DatasetFailure("Symbolic output ancestors are unsupported.");
			Directory.CreateDirectory(parent);
			stage = Path.Combine(parent, ".semantic-incoming-" + Guid.NewGuid().ToString("N"));
			if (SymbolicAncestor(stage)) return new DatasetFailure("Symbolic output ancestors are unsupported.");
			Directory.CreateDirectory(stage);
			using (var writer = new StreamWriter(Path.Combine(stage, "facts.jsonl"), false, Utf8))
			{
				writer.NewLine = "\n";
				foreach (var fact in facts)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var (type, value) = Scalar(fact.Value);
					writer.WriteLine(JsonSerializer.Serialize(new { subject = fact.Subject.Id, property = fact.Property, type, value, provenance = fact.Provenance }));
				}
			}
			using (var writer = new StreamWriter(Path.Combine(stage, "pages.jsonl"), false, Utf8))
			{
				writer.NewLine = "\n";
				foreach (var page in snapshot.Pages)
				{
					cancellationToken.ThrowIfCancellationRequested();
					writer.WriteLine(JsonSerializer.Serialize(new { id = page.Reference.Id, key = page.Key, title = page.Title, collections = page.Collections }));
				}
			}
			var licenses = new[] { "NOTICE.md", "README.md", "wiki/help/orc_license_notice.md" };
			Directory.CreateDirectory(Path.Combine(stage, "licenses"));
			foreach (var license in licenses)
				File.WriteAllText(Path.Combine(stage, "licenses", Path.GetFileName(license)), dataset.Content[license], Utf8);
			var report = new
			{
				package = "pf2e-remaster",
				version = "1.1.1",
				sourceCommit = PinnedDataset.Commit,
				extractionRuleVersion = Pf2eExtractor.RuleVersion,
				verification = "4714 declared/required files, 4560161 bytes; SHA256 and Git blob SHA1 equal the checked lock reviewed against the pinned Git tree. Extra undeclared files are not authenticated or read.",
				identity = "Opaque deterministic initial offline fixture IDs only. Production rename/recreation must use retained mappings and explicit migrations/new incarnation IDs.",
				pages = snapshot.Pages.Length,
				facts = facts.Length,
				coverage,
				unknown = extracted.Unknowns,
				rejects = Array.Empty<string>(),
				missing = "MissingMainSubjects counts absence of authored supported evidence, never level zero, false or inferred applicability.",
				licenses
			};
			File.WriteAllText(Path.Combine(stage, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n", Utf8);
			cancellationToken.ThrowIfCancellationRequested();
			if (SymbolicAncestor(destination)) return new DatasetFailure("Symbolic output ancestors are unsupported.");
			Directory.Move(stage, destination);
			stage = null;
			return new DryRunSuccess(snapshot.Pages.Length, facts.Length);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return new DatasetFailure("Dry run failed: " + exception.GetType().Name);
		}
		finally
		{
			if (stage is not null && Directory.Exists(stage)) Directory.Delete(stage, true);
		}
	}
	// Conservative preflight/rechecks, not a hostile concurrent-filesystem transaction guarantee.
	private static bool SymbolicAncestor(string path)
	{
		for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
			if (directory.LinkTarget is not null || directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
				return true;
		return false;
	}

	private static (string Type, object Value) Scalar(SemanticValue value) => value switch
	{
		PageRef reference => ("PageRef", reference.Id),
		long integer => ("Integer", integer),
		bool boolean => ("Boolean", boolean),
		Keyword keyword => ("Keyword", keyword.Value),
		_ => throw new InvalidOperationException("Canonical value has an unsupported type.")
	};
}
