using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics.Measurements;

/// <summary>
/// Phase C against the actual pack: on a world <c>build</c> made from a dataset, every supported shape for every
/// measured reader is paged to its end and compared with an oracle that never reads the projection. The oracle
/// takes the dataset's facts, the live page and <see cref="IWikiAccessService"/>, in the order the query orders
/// rows: subject (creation) order for an equality driver, the driving integer then subject for a range driver.
/// Unlike the load passes this one judges: any difference fails it.
/// </summary>
public partial class SemanticLoadMeasurement
{
	private sealed record OracleSubject(int Order, long? Level, long? Rank, HashSet<string> Traditions, HashSet<string> Classes, string? Ancestry);
	private sealed record OracleShape(string Name, SemanticWikiQuery Query, Func<OracleSubject, bool> Holds, string? Driver, string? Target);
	private sealed record OracleOutcome(string Shape, int Reader, string Result, int Rows, string? Detail);

	private async Task CompareWithOracleAsync(SemanticLoadSpec spec)
	{
		var build = JsonDocument.Parse(File.ReadAllText(Path.Join(spec.World!, "build.json"))).RootElement;
		var ids = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Join(spec.World!, "ids.json")))!;
		var host = SemanticWikiPrototypeHost.AdoptAfterOwnerExit(build.GetProperty("WorldPath").GetString()!,
			MySqlTestServer.Instance.GetConnectionString(), ConfigFile);
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var access = host.Services.GetRequiredService<IWikiAccessService>();

		// Pages were created in dataset order, so creation order is native subject order.
		var order = ids.Values.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
		var subjects = new Dictionary<string, OracleSubject>();
		foreach (var fact in Lines<DatasetFact>(Path.Join(spec.Dataset, "facts.jsonl")))
		{
			if (!ids.TryGetValue(fact.Subject, out var subject) || Convert(fact, subject, ids) is not { } converted) continue;
			if (!subjects.TryGetValue(subject, out var s)) subjects[subject] = s = new(order[subject], null, null, [], [], null);
			subjects[subject] = (converted.Property, converted.Value) switch
			{
				("pf2e:level", long level) => s with { Level = level },
				("pf2e:rank", long rank) => s with { Rank = rank },
				("pf2e:tradition", Keyword keyword) => Add(s, s.Traditions, keyword.Value),
				("pf2e:class", PageRef target) => Add(s, s.Classes, target.Id),
				("pf2e:ancestry", PageRef target) => s with { Ancestry = target.Id },
				_ => s
			};
		}
		var visible = new Dictionary<(string, int), bool>();
		async Task<bool> Visible(string id, int reader)
		{
			if (visible.TryGetValue((id, reader), out var known)) return known;
			return visible[(id, reader)] = await wiki.GetByIdAsync(id) is WikiPage live && await access.CanSeeAsync(Readers[reader], live);
		}

		var outcomes = new List<OracleOutcome>();
		foreach (var shape in OracleShapes(subjects))
			for (var reader = 0; reader < Readers.Length; reader++)
				outcomes.Add(await CompareShapeAsync(provider, shape, reader, subjects, Visible));

		var differing = outcomes.Where(o => o.Result is "mismatch" or "unexpected").ToList();
		await File.WriteAllTextAsync(Path.Join(spec.Output, "oracle.json"), JsonSerializer.Serialize(new
		{
			spec.Label,
			Subjects = subjects.Count,
			Comparisons = outcomes.Count,
			Results = outcomes.GroupBy(o => o.Result).ToDictionary(g => g.Key, g => g.Count()),
			RowsCompared = outcomes.Where(o => o.Result == "match").Sum(o => o.Rows),
			Differing = differing,
			Refined = outcomes.Where(o => o.Result == "refine").Select(o => $"{o.Shape} / reader {o.Reader}")
		}, Json));
		await host.CloseAsync();
		await Assert.That(differing).IsEmpty();
	}

	private static OracleSubject Add(OracleSubject subject, HashSet<string> set, string value)
	{
		set.Add(value);
		return subject;
	}

	private static IEnumerable<OracleShape> OracleShapes(IReadOnlyDictionary<string, OracleSubject> subjects)
	{
		SemanticWikiQuery Q(params SemanticPredicate[] predicates) => new("game", predicates, ["pf2e:level"], 50);
		foreach (var target in subjects.Values.SelectMany(s => s.Classes).Distinct().Order(StringComparer.Ordinal))
		{
			yield return new($"class={target}", Q(new SemanticPredicate.PageEquals("pf2e:class", target)), s => s.Classes.Contains(target), null, target);
			yield return new($"class={target} & level 1-10", Q(new SemanticPredicate.PageEquals("pf2e:class", target),
				new SemanticPredicate.IntegerRange("pf2e:level", 1, 10)), s => s.Classes.Contains(target) && s.Level is >= 1 and <= 10, null, target);
		}
		foreach (var target in subjects.Values.Select(s => s.Ancestry).OfType<string>().Distinct().Order(StringComparer.Ordinal))
			yield return new($"ancestry={target}", Q(new SemanticPredicate.PageEquals("pf2e:ancestry", target)), s => s.Ancestry == target, null, target);
		foreach (var tradition in new[] { "arcane", "divine", "occult", "primal" })
		{
			yield return new($"tradition={tradition}", Q(new SemanticPredicate.KeywordEquals("pf2e:tradition", tradition)), s => s.Traditions.Contains(tradition), null, null);
			yield return new($"tradition={tradition} & rank 1-5", Q(new SemanticPredicate.KeywordEquals("pf2e:tradition", tradition),
				new SemanticPredicate.IntegerRange("pf2e:rank", 1, 5)), s => s.Traditions.Contains(tradition) && s.Rank is >= 1 and <= 5, null, null);
		}
		for (var low = 0L; low <= 24; low += 4)
		{
			var (from, to) = (low, low + 3);
			yield return new($"level {from}-{to}", Q(new SemanticPredicate.IntegerRange("pf2e:level", from, to)), s => s.Level >= from && s.Level <= to, "pf2e:level", null);
		}
		yield return new("rank 1-10", Q(new SemanticPredicate.IntegerRange("pf2e:rank", 1, 10)), s => s.Rank is >= 1 and <= 10, "pf2e:rank", null);
		yield return new("level = 1", Q(new SemanticPredicate.IntegerEquals("pf2e:level", 1)), s => s.Level == 1, null, null);
		yield return new(SemanticLists.ArcaneByLevel.Name, SemanticLists.ArcaneByLevel.Query,
			s => s.Traditions.Contains("arcane") && s.Level is >= 1 and <= 10, "pf2e:level", null);
	}

	private static async Task<OracleOutcome> CompareShapeAsync(LightningDatabase provider, OracleShape shape, int reader,
		IReadOnlyDictionary<string, OracleSubject> subjects, Func<string, int, Task<bool>> visible)
	{
		var targetVisible = shape.Target is null || await visible(shape.Target, reader);
		var expected = new List<(string Id, OracleSubject Subject)>();
		if (targetVisible)
			foreach (var (id, subject) in subjects)
				if (shape.Holds(subject) && await visible(id, reader)) expected.Add((id, subject));
		IEnumerable<(string Id, OracleSubject Subject)> ordered = shape.Driver switch
		{
			"pf2e:level" => expected.OrderBy(e => e.Subject.Level).ThenBy(e => e.Subject.Order),
			"pf2e:rank" => expected.OrderBy(e => e.Subject.Rank).ThenBy(e => e.Subject.Order),
			_ => expected.OrderBy(e => e.Subject.Order)
		};
		var want = ordered.Select(e => e.Id).ToList();

		var got = new List<string>();
		string? token = null;
		do
		{
			var result = await SemanticWikiQueries.RunAsync(provider, $"#oracle{reader}", _ => ValueTask.FromResult(Readers[reader]), shape.Query, token, CancellationToken.None);
			switch (result)
			{
				case SemanticQueryResult.Page page:
					got.AddRange(page.Rows.Select(r => r.PageId));
					token = page.Continuation;
					break;
				case SemanticQueryResult.NotCertified { Outcome: var outcome } when got.Count == 0 && outcome.GetType().Name == "Unavailable":
					return new(shape.Name, reader, targetVisible ? "unexpected" : "unavailable", 0, targetVisible ? "Unavailable for a visible target" : null);
				case SemanticQueryResult.RefineRequired refine:
					return new(shape.Name, reader, "refine", got.Count, refine.Budget);
				default:
					return new(shape.Name, reader, "unexpected", got.Count, result.ToString());
			}
		} while (token is not null);
		if (!targetVisible) return new(shape.Name, reader, "unexpected", got.Count, "Rows for a target the reader cannot see");
		if (got.SequenceEqual(want)) return new(shape.Name, reader, "match", got.Count, null);
		var first = Enumerable.Range(0, Math.Min(got.Count, want.Count)).FirstOrDefault(i => got[i] != want[i], Math.Min(got.Count, want.Count));
		return new(shape.Name, reader, "mismatch", got.Count,
			$"got {got.Count}, want {want.Count}; first difference at {first}: {got.ElementAtOrDefault(first)} vs {want.ElementAtOrDefault(first)}");
	}
}
