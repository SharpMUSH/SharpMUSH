using SharpMUSH.Library.Models.Packages;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Tools.WikiSemantics;
namespace SharpMUSH.Tests.Wiki.Semantics;

public class SemanticExtractionTests
{
	[Test]
	public async Task AuthoredFieldsPreserveRankMulticlassAndUnknownAncestry()
	{
		var poison = Pf2eExtractor.ReadFields("**Class Feat 2**  \n**Traits:** [[Alchemist|main:alchemist]], [[Druid|main:druid]]\n\n*Source: Player Core.*\n");
		await Assert.That(poison.Level).IsEqualTo(2L);
		await Assert.That(string.Join(",", poison.Classes)).IsEqualTo("alchemist,druid");
		var spell = Pf2eExtractor.ReadFields("**Cantrip 1**\n**Traditions:** [[Arcane|category:arcane_spells]], [[Primal|category:primal_spells]]\n");
		await Assert.That(spell.Rank).IsEqualTo(1L);
		await Assert.That(spell.Level is null).IsTrue();
		await Assert.That(Pf2eExtractor.ReadFields("**Heritage**\n**Ancestry:** any (a versatile heritage)\n").UnknownAncestry).IsTrue();
		await Assert.That(Pf2eExtractor.ReadFields("## Class feats\n- **Class Feat 8**\n").Level is null).IsTrue();
	}

	[Test]
	[Explicit]
	public async Task VerifiedFullCorpusAgreesWithAuthoredHubAndDwarfHeritages()
	{
		var source = Environment.GetEnvironmentVariable("SEMANTIC_PF2E_SOURCE") ?? throw new InvalidOperationException("Set SEMANTIC_PF2E_SOURCE to the pinned external corpus; this is required acceptance, not a sample test.");
		var dataset = PinnedDataset.Load(source).Expect<VerifiedDataset>();
		var snapshot = Pf2eExtractor.Extract(dataset).Expect<ExtractionSuccess>().Snapshot;
		var alchemist = snapshot.Pages.Single(p => p.Key.Namespace == "main" && p.Key.SourceKey == "alchemist").Reference;
		var query = new SemanticQuery(Pf2eExtractor.Collection, [new EqualPredicate("pf2e:class", alchemist)], ["pf2e:level"], new IntegerSort("pf2e:level"), 100, IncludeCount: true);
		var result = ReferenceEvaluator.Evaluate(snapshot, query, WikiVisibility.All).Expect<QuerySuccess>();
		await Assert.That(result.Count).IsEqualTo(47L);
		await Assert.That(result.HasMore).IsFalse();
		// Independent authored hub evidence: these Level list rows are never interpreted by the extractor.
		var expected = new SortedDictionary<string, long>(StringComparer.Ordinal);
		foreach (var line in dataset.Content["wiki/category/alchemist_feats.md"].Split('\n'))
		{
			var level = Regex.Match(line, @"^- \*\*Level ([0-9]+):\*\* ");
			if (!level.Success) continue;
			foreach (Match link in Regex.Matches(line, @"\[\[[^|]+\|main:([^\]]+)\]\]"))
				expected.Add(link.Groups[1].Value, long.Parse(level.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
		}
		var actual = new SortedDictionary<string, long>(StringComparer.Ordinal);
		foreach (var row in result.Rows)
		{
			var key = snapshot.Pages.Single(p => p.Reference == row.Subject).Key.SourceKey;
			actual.Add(key, row.Cells.Single().Values.Single().Expect<long>());
		}
		await Assert.That(expected.Count).IsEqualTo(47);
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual)).IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected));
		var dwarf = snapshot.Pages.Single(p => p.Key.Namespace == "main" && p.Key.SourceKey == "dwarf").Reference;
		var heritages = ReferenceEvaluator.Evaluate(snapshot, query with { Predicates = [new EqualPredicate("pf2e:ancestry", dwarf)], Columns = [], Sort = new IdSort() }, WikiVisibility.All).Expect<QuerySuccess>();
		var dwarfKeys = heritages.Rows.Select(r => snapshot.Pages.Single(p => p.Reference == r.Subject).Key.SourceKey).Order(StringComparer.Ordinal);
		await Assert.That(string.Join(",", dwarfKeys)).IsEqualTo("ancient_blooded_dwarf,death_warden_dwarf,forge_dwarf,rock_dwarf,strong_blooded_dwarf");
		await Assert.That(heritages.Count).IsEqualTo(5L);
	}

	[Test]
	public async Task UnsupportedNumericHeaderIsUnknownInsteadOfThrowing()
	{
		await Assert.That(Pf2eExtractor.ReadFields("**Item 99999999999999999999999999999999**\n").Level is null).IsTrue();
	}

	[Test]
	[Explicit]
	public async Task VerifiedPageDeclarationsCannotChangeAfterValidation()
	{
		var source = Environment.GetEnvironmentVariable("SEMANTIC_PF2E_SOURCE") ?? throw new InvalidOperationException("Set SEMANTIC_PF2E_SOURCE for actual corpus acceptance.");
		var dataset = PinnedDataset.Load(source).Expect<VerifiedDataset>();
		IReadOnlyList<PackageWikiPageSpec> pages = dataset.Pages;
		if (pages is IList<PackageWikiPageSpec> editable && !editable.IsReadOnly)
			editable[0] = pages[0] with { Title = "post-verification replacement" };
		if (pages.First(p => p.Categories.Count > 0).Categories is IList<string> categories && !categories.IsReadOnly)
			categories.Add("post-verification category");
		var snapshot = Pf2eExtractor.Extract(dataset).Expect<ExtractionSuccess>().Snapshot;
		await Assert.That(snapshot.Pages.Any(p => p.Title == "post-verification replacement" || p.Visibility.Categories.Contains("post-verification category"))).IsFalse();
	}
}
