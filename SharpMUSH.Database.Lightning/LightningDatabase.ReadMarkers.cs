using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IReadMarkerStore"/>: one <see cref="Tables.ReadMarker"/> row per character and scope, keyed
/// dbref + 0x00 + scope, so a character's markers are one prefix range and the object-delete cascade drops
/// them with the rest of the object's rows.
/// </summary>
public partial class LightningDatabase
{
	public ValueTask<IReadOnlyList<SharpReadMarker>> GetReadMarkersAsync(DBRef character,
		CancellationToken cancellationToken = default)
	{
		var creation = CreationOf(character);
		IReadOnlyList<SharpReadMarker> markers = Store.Read(tx => tx.Range(Tables.ReadMarker, Keys.Composite(character.Number, ""))
			.Select(entry => Codec.Deserialize<ReadMarkerRecord>(entry.Value))
			.Where(record => record.CharacterCreationTime == creation)
			.Select(ToMarker)
			.ToList());
		return ValueTask.FromResult(markers);
	}

	public async ValueTask<SharpReadMarker> AdvanceReadMarkerAsync(DBRef character, SharpReadMarker marker,
		CancellationToken cancellationToken = default)
	{
		var creation = CreationOf(character);
		var key = Keys.Composite(character.Number, marker.Scope);
		return await Store.WriteAsync(tx =>
		{
			// A row left by an earlier holder of this dbref is not this character's, and is replaced.
			if (tx.TryGet(Tables.ReadMarker, key, out var bytes)
				&& Codec.Deserialize<ReadMarkerRecord>(bytes) is { } existing
				&& existing.CharacterCreationTime == creation
				&& !marker.IsPast(ToMarker(existing)))
			{
				return ToMarker(existing);
			}

			tx.Put(Tables.ReadMarker, key, Codec.Serialize(new ReadMarkerRecord
			{
				CharacterCreationTime = creation,
				Scope = marker.Scope,
				LastReadId = marker.LastReadId,
				LastReadAtMs = marker.LastReadAt.ToUnixTimeMilliseconds()
			}));
			return marker with { LastReadAt = DateTimeOffset.FromUnixTimeMilliseconds(marker.LastReadAt.ToUnixTimeMilliseconds()) };
		}, cancellationToken);
	}

	private static long CreationOf(DBRef character) =>
		character.CreationMilliseconds
		?? throw new ArgumentException($"Read markers belong to a character by objid; {character} has no creation time.",
			nameof(character));

	private static SharpReadMarker ToMarker(ReadMarkerRecord record) =>
		new(record.Scope, record.LastReadId, DateTimeOffset.FromUnixTimeMilliseconds(record.LastReadAtMs));
}
