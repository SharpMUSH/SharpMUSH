using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// What each wiki namespace, category and page requires (<see cref="WikiRequirementSet"/>), one row per
/// target in <see cref="Tables.WikiRequirement"/>, keyed by the scope's lowercase name and the target's key.
/// A page's row goes with the page.
/// </summary>
public partial class LightningDatabase
{
	private static byte[] WikiRequirementKey(WikiRuleScope scope, string key)
		=> Keys.Composite(scope.ToString().ToLowerInvariant(), key);

	public Task<IReadOnlyList<WikiRequirementSet>> GetRequirementsAsync()
		=> Task.FromResult<IReadOnlyList<WikiRequirementSet>>(Store.Read(tx =>
			tx.Range(Tables.WikiRequirement, [])
				.Select(entry => MapWikiRequirement(Codec.Deserialize<WikiRequirementRecord>(entry.Value)))
				.OfType<WikiRequirementSet>()
				.ToList()));

	public async Task<Found<None>> SetRequirementsAsync(WikiRequirementSet set)
	{
		using var admission = EnterFixtureMutation(SemanticFixtureMutation.Requirements);
		var key = set.Target.Scope == WikiRuleScope.Page ? CanonicalWikiPageId(set.Target.Key) : set.Target.Key;
		return await WriteFixtureAsync<Found<None>>(admission, tx =>
		{
			if (set.Target.Scope == WikiRuleScope.Page && TryReadWikiPage(tx, key) is null) return new NotFound();

			var rowKey = WikiRequirementKey(set.Target.Scope, key);
			// An active projection admits only edit and delete requirements, which no visibility descriptor holds.
			if (admission is { Phase: SemanticFixturePhase.Active })
			{
				var current = tx.TryGet(Tables.WikiRequirement, rowKey, out var bytes)
					? MapWikiRequirement(Codec.Deserialize<WikiRequirementRecord>(bytes)) : null;
				if (!SameRead(current, set)) throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
			}
			if (set.IsEmpty)
			{
				tx.Delete(Tables.WikiRequirement, rowKey);
				return new None();
			}

			tx.Put(Tables.WikiRequirement, rowKey, Codec.Serialize(new WikiRequirementRecord
			{
				Scope = set.Target.Scope.ToString().ToLowerInvariant(),
				Key = key,
				Required = set.Required
					.Where(pair => pair.Value.Count > 0)
					.ToDictionary(pair => pair.Key.ToString().ToLowerInvariant(), pair => pair.Value.ToArray()),
				UpdatedBy = set.UpdatedBy,
				UpdatedAt = set.UpdatedAt.ToUnixTimeMilliseconds(),
			}));
			return new None();
		});
	}

	private static bool SameRead(WikiRequirementSet? current, WikiRequirementSet next)
	{
		IReadOnlyList<string> Read(WikiRequirementSet? set) => set is not null && set.Required.TryGetValue(WikiAction.Read, out var scopes) ? scopes : [];
		return Read(current).SequenceEqual(Read(next), StringComparer.Ordinal);
	}

	/// <summary>True when the page itself carries requirements, which keeps its full revision history.</summary>
	private static bool WikiPageHasRequirements(ITx tx, string pageId)
		=> tx.TryGet(Tables.WikiRequirement, WikiRequirementKey(WikiRuleScope.Page, CanonicalWikiPageId(pageId)), out _);

	/// <summary>The set a row holds; null for a row naming a scope or action this build does not know.</summary>
	internal static WikiRequirementSet? MapWikiRequirementForQueries(WikiRequirementRecord? record)
		=> record is null ? null : MapWikiRequirement(record);

	private static WikiRequirementSet? MapWikiRequirement(WikiRequirementRecord record)
	{
		if (!Enum.TryParse<WikiRuleScope>(record.Scope, ignoreCase: true, out var scope)) return null;
		var required = new Dictionary<WikiAction, IReadOnlyList<string>>();
		foreach (var (name, scopes) in record.Required)
		{
			if (Enum.TryParse<WikiAction>(name, ignoreCase: true, out var action)) required[action] = scopes;
		}

		return new WikiRequirementSet(new WikiRuleTarget(scope, record.Key), required, record.UpdatedBy,
			DateTimeOffset.FromUnixTimeMilliseconds(record.UpdatedAt));
	}
}
