using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// A player's aliases are its <c>ALIAS</c> attribute (<see cref="PlayerAliases"/>). Player lookup reads
/// <see cref="Tables.ObjName"/>, so the record's <see cref="ObjectRecord.Aliases"/> and the index rows for
/// them are derived from that attribute here, inside the transaction that wrote it: every attribute write
/// that can touch <c>ALIAS</c> calls <see cref="SyncPlayerAliases"/>, which is PennMUSH's
/// <c>reset_player_list</c> (<c>src/plyrlist.c</c>) for this store.
/// </summary>
public partial class LightningDatabase
{
	/// <summary>Whether a write to <paramref name="path"/> can change an object's alias list.</summary>
	private static bool TouchesAliases(string[] path)
		=> path.Length > 0 && PlayerAliases.IsAliasAttribute(path[0]);

	/// <summary>
	/// Makes player <paramref name="dbref"/>'s indexed aliases those its <c>ALIAS</c> attribute holds now.
	/// Anything that is not a player has no player aliases (an exit's are part of its name), so it is left
	/// alone.
	/// </summary>
	internal void SyncPlayerAliases(ITx tx, long dbref)
	{
		if (ReadObject(tx, dbref) is not { Record: { Type: DatabaseConstants.TypePlayer } record })
		{
			return;
		}

		var value = ReadAttributeValue(tx, dbref, PlayerAliases.AttributeName);
		var aliases = value is null ? [] : PlayerAliases.Split(DeserializeValue(value).ToPlainText());

		if (record.Aliases.SequenceEqual(aliases, StringComparer.Ordinal))
		{
			return;
		}

		var key = Keys.Dbref(dbref);
		// One index row serves the name and an alias spelled the same way; the name keeps it.
		foreach (var old in record.Aliases.Where(old => !old.Equals(record.Name, StringComparison.OrdinalIgnoreCase)))
		{
			tx.Delete(Tables.ObjName, Keys.Lower(old), key);
		}

		foreach (var alias in aliases)
		{
			tx.Put(Tables.ObjName, Keys.Lower(alias), key);
		}

		tx.Put(Tables.Obj, key, Codec.Serialize(record with { Aliases = aliases }));
	}
}
