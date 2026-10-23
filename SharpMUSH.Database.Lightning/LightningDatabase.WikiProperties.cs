using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The properties a page's facts may state: one row per property id in <see cref="Tables.WikiProperty"/>. Every write
/// that reads them reads them in its own transaction, so a page's facts are typed by the properties as they stand.
/// </summary>
/// <remarks>
/// A property defined, changed or removed leaves the pages that state it alone: a page keeps a fact its property no
/// longer takes until someone changes that property's values. Its index entries move in the same transaction when
/// the change moves them between number and text order.
/// </remarks>
public partial class LightningDatabase
{
	private static SemanticSchema ReadFactSchema(ITx tx)
		=> WikiFactProperties.Schema(tx.Range(Tables.WikiProperty, [])
			.Select(entry => MapWikiProperty(Codec.Deserialize<WikiPropertyRecord>(entry.Value))));

	/// <summary>The properties whose values the index keys as numbers.</summary>
	private static HashSet<string> IntegerFactProperties(ITx tx)
		=> tx.Range(Tables.WikiProperty, [])
			.Select(entry => Codec.Deserialize<WikiPropertyRecord>(entry.Value))
			.Where(record => record.Type == nameof(SemanticType.Integer))
			.Select(record => record.Id)
			.ToHashSet(StringComparer.Ordinal);

	public Task<SemanticSchema> GetFactSchemaAsync() => Task.FromResult(Store.Read(ReadFactSchema));

	public async Task<Result<PropertyDefinition>> SetFactPropertyAsync(PropertyDefinition property)
		=> WikiFactProperties.Checked(property) switch
		{
			PropertyDefinition definition => await Store.WriteAsync<Result<PropertyDefinition>>(tx =>
			{
				var key = Keys.Str(definition.Id);
				var wasInteger = tx.TryGet(Tables.WikiProperty, key, out var bytes)
					&& Codec.Deserialize<WikiPropertyRecord>(bytes).Type == nameof(SemanticType.Integer);
				tx.Put(Tables.WikiProperty, key, Codec.Serialize(WikiPropertyRecordOf(definition)));
				if (wasInteger != (definition.Type == SemanticType.Integer)) RetypeFactEntries(tx, definition.Id);
				return definition;
			}),
			Error<string> error => error,
		};

	public async Task<Found<None>> DeleteFactPropertyAsync(string id)
		=> id.Length is 0 or > WikiFactProperties.MaxIdLength
			? new NotFound()
			: await Store.WriteAsync<Found<None>>(tx =>
			{
				var key = Keys.Str(id);
				if (!tx.TryGet(Tables.WikiProperty, key, out var bytes)) return new NotFound();
				tx.Delete(Tables.WikiProperty, key);
				if (Codec.Deserialize<WikiPropertyRecord>(bytes).Type == nameof(SemanticType.Integer)) RetypeFactEntries(tx, id);
				return new None();
			});

	/// <summary>
	/// <paramref name="property"/>'s index entries keyed again under its type as it now stands, page by page from the
	/// entries it has. A page the build has not reached yet has none, and is keyed when the build reaches it.
	/// </summary>
	private static void RetypeFactEntries(ITx tx, string property)
	{
		if (!ReadFactIndexState(tx).Maintained) return;
		var entries = tx.Range(Tables.WikiFact, FactPropertyPrefix(property)).Select(entry => entry.Key).ToList();
		foreach (var key in entries) tx.Delete(Tables.WikiFact, key);

		var integers = IntegerFactProperties(tx);
		var pages = entries.Select(key => Keys.ReadDbref(key.AsSpan(key.Length - 8))).Distinct()
			.Select(pageKey => (Key: pageKey, Record: TryReadWikiPage(tx, pageKey)))
			.Where(page => page.Record is { Facts: not null });
		foreach (var (pageKey, record) in pages)
			PutFactEntries(tx, pageKey, [.. record!.Facts!.Where(f => f.Property == property)], add: true, integers);
	}

	private static PropertyDefinition MapWikiProperty(WikiPropertyRecord record)
		=> new(record.Id, WikiFactProperties.SchemaVersion,
			Enum.TryParse<SemanticType>(record.Type, out var type) ? type : SemanticType.Keyword,
			record.Multiple ? Cardinality.Multiple : Cardinality.Single,
			[.. (record.Keywords ?? []).Select(keyword => new Keyword(keyword))],
			record.Label ?? string.Empty, record.Description ?? string.Empty);

	private static WikiPropertyRecord WikiPropertyRecordOf(PropertyDefinition definition) => new()
	{
		Id = definition.Id,
		Type = definition.Type.ToString(),
		Multiple = definition.Cardinality == Cardinality.Multiple,
		Keywords = definition.AllowedKeywords.IsDefaultOrEmpty ? null : [.. definition.AllowedKeywords.Select(k => k.Value)],
		Label = definition.Label.Length == 0 ? null : definition.Label,
		Description = definition.Description.Length == 0 ? null : definition.Description,
	};

	/// <summary>How a fact the properties refuse is told.</summary>
	private static string DescribeRefusedFact(ValidationFailure failure) => failure.Code switch
	{
		ValidationCode.UnknownProperty => $"No property {failure.Detail} is defined.",
		ValidationCode.InvalidEnum => $"{failure.Detail} does not allow that value.",
		ValidationCode.InvalidKeyword => $"{failure.Detail} has a value that is not a keyword.",
		ValidationCode.Cardinality => $"{failure.Detail} takes one value.",
		_ => failure.Detail
	};
}
