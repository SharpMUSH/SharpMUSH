using System.Buffers.Binary;
using LightningDB;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// Issued fixtures only: durable counters a semantic reader pins in its first section and checks in every
/// later one. Each advances once per writer job that changes a table of its class, in that same job, so no
/// committed change of that class can leave its counter where a reader pinned it.
/// </summary>
internal enum FixtureVersionClass
{
	/// <summary>Wiki pages, revisions, slugs, translations, namespace and category membership, pins, and the semantic projection.</summary>
	Content,
	/// <summary>Namespace, category and page requirements.</summary>
	Requirements,
	/// <summary>What decides a reader's scopes: accounts, their characters, roles, grants, permission overrides and definitions.</summary>
	Authorization
}

public sealed partial class LightningStore
{
	private static readonly byte[][] FixtureVersionKeys =
	[
		"prototype.semantic.version.content/v1"u8.ToArray(),
		"prototype.semantic.version.requirements/v1"u8.ToArray(),
		"prototype.semantic.version.authorization/v1"u8.ToArray()
	];

	internal static FixtureVersionClass? ClassifyFixtureTable(string name) => name switch
	{
		"wiki.req" => FixtureVersionClass.Requirements,
		// The fact index: no semantic read of a fixture reads it, so its writes move no reader's pin.
		"wiki.fact" => null,
		_ when name.StartsWith("wiki.", StringComparison.Ordinal)
			|| name.StartsWith(AtomicWikiLayout.Prefix, StringComparison.Ordinal) => FixtureVersionClass.Content,
		"account" or "acct.perm" or "acct.ban" or "obj.perm" or "role" or "role.cat" or "perm.def" or "perm.cat"
			or "e.acct.role" or "e.acct.char" or "r.acct.char" or "e.obj.role" or "r.obj.role" => FixtureVersionClass.Authorization,
		_ => null
	};

	/// <summary>The current value of each counter, read in <paramref name="tx"/>'s own snapshot; a missing counter is zero.</summary>
	internal static ulong ReadFixtureVersion(ITx tx, FixtureVersionClass version)
	{
		if (!tx.TryGet(Tables.Meta, FixtureVersionKeys[(int)version], out var value)) return 0;
		if (value.Length != 8) throw SemanticFixtureCapability.Unavailable();
		return BinaryPrimitives.ReadUInt64BigEndian(value);
	}

	private static bool IsFixtureVersionKey(ReadOnlySpan<byte> key, bool prefixDelete)
	{
		foreach (var protectedKey in FixtureVersionKeys)
			if (prefixDelete ? protectedKey.AsSpan().StartsWith(key) : protectedKey.AsSpan().SequenceEqual(key)) return true;
		return false;
	}

	private sealed partial class Tx
	{
		private int _fixtureVersionsAdvanced;

		/// <summary>Advances <paramref name="version"/> once in this job, before its first change of that class.</summary>
		private void AdvanceFixtureVersion(FixtureVersionClass version)
		{
			var bit = 1 << (int)version;
			if ((_fixtureVersionsAdvanced & bit) != 0) return;
			var meta = Db(Tables.Meta);
			var key = FixtureVersionKeys[(int)version];
			var (found, _, current) = tx.Get(meta, key);
			ulong previous = 0;
			if (found == MDBResultCode.Success)
			{
				if (CheckedLength(current) != 8) throw SemanticFixtureCapability.Unavailable();
				previous = BinaryPrimitives.ReadUInt64BigEndian(current.AsSpan());
			}
			else if (found != MDBResultCode.NotFound) throw LightningStoreException.From(found, "read fixture version");
			Span<byte> next = stackalloc byte[8];
			BinaryPrimitives.WriteUInt64BigEndian(next, checked(previous + 1));
			var code = tx.Put(meta, key, next);
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "advance fixture version");
			_fixtureVersionsAdvanced |= bit;
		}
	}
}
