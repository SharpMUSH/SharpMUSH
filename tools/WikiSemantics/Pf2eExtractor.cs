using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tools.WikiSemantics;

public sealed record AuthoredFields(long? Level, long? Rank, string[] Classes, string[] Traditions, string[] Ancestries,
	bool UnknownAncestry, string SourceCitation);
public sealed record ExtractionUnknown(PortableKey Key, string Property, string Reason);
public sealed record ExtractionSuccess(SemanticSnapshot Snapshot, ImmutableArray<ExtractionUnknown> Unknowns);
public union ExtractionResult(ExtractionSuccess, DatasetFailure);

/// <summary>Only anchored authored fields are interpreted; arbitrary rules prose is never evaluated.</summary>
public static class Pf2eExtractor
{
	public const string RuleVersion = "pf2e-authored-fields-1";
	public const string Collection = "package:pf2e-remaster";
	private static readonly HashSet<string> ClassIds = new("alchemist barbarian bard champion cleric druid fighter investigator monk oracle ranger rogue sorcerer swashbuckler witch wizard".Split(' '), StringComparer.Ordinal);
	private static readonly HashSet<string> AncestryIds = new("dwarf elf kholo orc leshy kobold gnome goblin catfolk lizardfolk ratfolk tengu hobgoblin halfling tripkee human".Split(' '), StringComparer.Ordinal);
	private static readonly HashSet<string> TraditionIds = new(["arcane", "divine", "occult", "primal"], StringComparer.Ordinal);
	private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(1);
	private static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.CultureInvariant, RegexBudget);
	private static string Field(string header, string name) => Match(header, @"(?m)^\*\*" + name + @":\*\* ([^\r\n]+)").Groups[1].Value.Trim();
	private static string[] Links(string value, string space) => Regex.Matches(value, @"\[\[[^|\]\r\n]+\|" + space + @":([a-z0-9_]+)\]\]", RegexOptions.CultureInvariant, RegexBudget)
		.Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

	public static AuthoredFields ReadFields(string body)
	{
		// Author metadata ends at the first blank line. A prose field elsewhere cannot become a fact.
		var header = body.Split("\n\n", 2, StringSplitOptions.None)[0];
		var level = Match(header, @"\A\*\*(?:(?:Archetype Skill|Archetype|Ancestry|Class|General|Skill) Feat|Class Feature|Item) ([0-9]+)\*\*(?:[ \t]*(?:\([^\r\n]*\))?)?[ \t]*(?:\r?\n|$)");
		var rank = Match(header, @"\A\*\*(?:Spell|Focus|Cantrip|Ritual) ([0-9]+)\*\*[ \t]*(?:\r?\n|$)");
		var classes = header.StartsWith("**Class Feat ", StringComparison.Ordinal)
			? Links(Field(header, "Traits"), "main").Where(ClassIds.Contains).ToArray() : [];
		var traditions = Links(Field(header, "Traditions"), "category").Select(x => x.EndsWith("_spells", StringComparison.Ordinal) ? x[..^7] : "")
			.Where(TraditionIds.Contains).ToArray();
		var ancestryField = header.StartsWith("**Heritage**", StringComparison.Ordinal) ? Field(header, "Ancestry") : "";
		var ancestries = Links(ancestryField, "main").Where(AncestryIds.Contains).ToArray();
		var citation = Match(body, @"(?m)^\*Source: ([^\r\n]+)\*[ \t]*\r?$").Groups[1].Value;
		return new(level.Success && long.TryParse(level.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLevel) ? parsedLevel : null,
			rank.Success && long.TryParse(rank.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedRank) ? parsedRank : null,
			classes, traditions, ancestries, ancestryField.Length > 0 && ancestries.Length == 0, citation);
	}

	public static SemanticSchema Schema { get; } = new(1,
	[
		new("pf2e:level", 1, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:rank", 1, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:tradition", 1, SemanticType.Keyword, Cardinality.Multiple, [new("arcane"), new("divine"), new("occult"), new("primal")]),
		new("pf2e:class", 1, SemanticType.PageRef, Cardinality.Multiple, []),
		new("pf2e:ancestry", 1, SemanticType.PageRef, Cardinality.Single, []),
		new("pf2e:source-book", 1, SemanticType.Keyword, Cardinality.Single, [new("player core"), new("player core 2"), new("gm core")])
	]);

	public static ExtractionResult Extract(VerifiedDataset dataset, CancellationToken cancellationToken = default)
	{
		var stamp = new ExperimentalStamp(PinnedDataset.Commit, 1, 1, 1, 1, 1, 1, 1);
		var pages = dataset.Pages.OrderBy(p => p.Label, StringComparer.Ordinal).Select(p =>
		{
			var key = new PortableKey("pf2e-remaster", p.NamespaceName, p.Page);
			// Deterministic initial OFFLINE fixture IDs only; never a production import/update identity algorithm.
			var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(PinnedDataset.Commit + "\n" + key.Package + "\n" + key.Namespace + "\n" + key.SourceKey)));
			return new PageSnapshot(new(id), key, p.Title, dataset.Content[p.File!], [Collection],
				new(p.NamespaceName, [.. p.Categories], [], p.Published, null), stamp);
		}).ToImmutableArray();
		var refs = pages.ToDictionary(p => p.Key.Namespace + ":" + p.Key.SourceKey, p => p.Reference, StringComparer.Ordinal);
		var specs = dataset.Pages.ToDictionary(p => p.Label, StringComparer.Ordinal);
		var facts = new List<SemanticFact>();
		var unknown = ImmutableArray.CreateBuilder<ExtractionUnknown>();
		foreach (var page in pages.Where(p => p.Key.Namespace == "main"))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var fields = ReadFields(page.Body);
			var path = specs[page.Key.Namespace + ":" + page.Key.SourceKey].File!;
			void Add(string property, SemanticValue value, string field, string evidence)
				=> facts.Add(new(page.Reference, property, value, new("pf2e-remaster", PinnedDataset.Commit, path, field, false,
					"1.1.1", dataset.Blobs[path], RuleVersion, evidence)));
			if (fields.Level is long level) Add("pf2e:level", level, "leading authored level", page.Body.Split('\n')[0]);
			if (fields.Rank is long rank) Add("pf2e:rank", rank, "leading authored rank", page.Body.Split('\n')[0]);
			foreach (var tradition in fields.Traditions) Add("pf2e:tradition", new Keyword(tradition), "Traditions", Field(page.Body, "Traditions"));
			foreach (var classId in fields.Classes) Add("pf2e:class", refs["main:" + classId], "Class Feat Traits", Field(page.Body, "Traits"));
			foreach (var ancestry in fields.Ancestries) Add("pf2e:ancestry", refs["main:" + ancestry], "Heritage Ancestry", Field(page.Body, "Ancestry"));
			if (fields.UnknownAncestry) unknown.Add(new(page.Key, "pf2e:ancestry", "Authored ancestry is versatile or unsupported; no reference inferred."));
			if (fields.SourceCitation.Length > 0)
			{
				var book = Match(fields.SourceCitation, @"\A(Player Core 2|Player Core|GM Core)(?:[.,]|$)").Groups[1].Value;
				if (book.Length > 0) Add("pf2e:source-book", new Keyword(book), "Source footer", fields.SourceCitation);
				else unknown.Add(new(page.Key, "pf2e:source-book", "Unsupported source footer: " + fields.SourceCitation));
			}
		}
		return CanonicalFacts.Validate(Schema, facts, cancellationToken) switch
		{
			CanonicalFactSet canonical => new ExtractionSuccess(new(Schema, pages, canonical, stamp), unknown.ToImmutable()),
			ValidationFailure failure => new DatasetFailure("Canonical extraction rejected: " + failure.Detail)
		};
	}
}
