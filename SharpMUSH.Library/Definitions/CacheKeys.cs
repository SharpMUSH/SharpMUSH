using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Canonical cache keys for object-scoped cache entries.
///
/// Keyed by the dbref NUMBER only — never the creation timestamp — so a bare "#N" reference and a full
/// "#N:creation" objid map to the SAME entry. Reads resolve objects via many paths that only know the
/// number (parsing "#N", lookups by number, God/config/connection refs), while every mutation knows the
/// object's number, so number-keying makes reads and the invalidation that must clear them always agree.
///
/// The objid (recycle) check — rejecting a full objid whose timestamp doesn't match the live object — is
/// applied OUTSIDE these cached entries (see <c>GetObjectNodeQueryHandler</c>), so it still runs on every
/// request instead of being bypassed on a cache hit.
/// </summary>
public static class CacheKeys
{
	public static string Object(int number) => $"object:#{number}";
	public static string Object(DBRef dbref) => Object(dbref.Number);

	/// <summary>
	/// Tag carried by every cached result that embeds object <paramref name="number"/> - a contents
	/// list, a location answer, a player-by-name lookup - stamped by the caching behaviours from the
	/// value itself. Removed whenever a command invalidates the object's own key, so a write to an
	/// object expires every entry holding a snapshot of it, not only its node.
	/// </summary>
	public static string ObjectTag(int number) => $"obj:#{number}";

	/// <summary>The object number an <see cref="Object(int)"/> key names, if it is one.</summary>
	public static bool TryParseObjectNumber(string key, out int number)
	{
		number = 0;
		return key.StartsWith("object:#", StringComparison.Ordinal)
			&& int.TryParse(key.AsSpan("object:#".Length), out number);
	}

	public static string Contents(int number) => $"object-contents:#{number}";
	public static string Contents(DBRef dbref) => Contents(dbref.Number);

	// Location entries. Depth is 1 in every current caller, but kept in the key for correctness; the
	// per-object location TAG (below) is how invalidation clears all of an object's location entries
	// regardless of depth.
	public static string Location(int number, int depth) => $"location:#{number}:d{depth}";
	public static string LocationByKey(string id, int depth) => $"location-key:{id}:d{depth}";

	/// <summary>Tag on GetLocationQuery (number-keyed); a move RemoveByTag's this to clear all depths.</summary>
	public static string LocationTag(int number) => $"loc:#{number}";
	/// <summary>Tag on GetCertainLocationQuery (graph-id keyed); a move RemoveByTag's this.</summary>
	public static string LocationTag(string id) => $"loc-id:{id}";

	/// <summary>
	/// One container's contents. Tagged as well as keyed: removing a key drops only what is cached at
	/// that instant, so a read that began before a write stores its pre-write list afterwards. Only a tag
	/// invalidation is resolved against when the reading factory started. Per container rather than the
	/// broad <see cref="CacheTags.ObjectContents"/>, because movement is the hot path.
	/// </summary>
	public static string ContentsTag(int number) => $"contents:#{number}";

	// ---------------------------------------------------------------------------------------------
	// Attributes.
	//
	// Readers and writers build their keys from the SAME helpers here, because for a long time they
	// did not: every attribute-mutating command spelled the key with a trailing ")" the readers never
	// had, and the path-wise commands keyed each segment rather than the joined path, so no attribute
	// write ever removed the entry it named. A single game-wide tag hid it. Anything that adds a new
	// attribute-shaped cache entry belongs in AttributesTouchedBy so a write keeps reaching it.
	// ---------------------------------------------------------------------------------------------

	// Number only, never the creation stamp, for the reason stated at the top of this class: the
	// reading side names an object through SharpObject.DBRef, which ALWAYS carries the stamp, while
	// the writing side is handed whatever the caller parsed — usually a bare "#N". Keyed by the whole
	// reference those two never met, so "commands:" invalidation missed for exactly as long as the
	// attribute keys did.

	/// <summary>An attribute path as it appears in a key: the segments joined by backticks.</summary>
	public static string AttributePath(string[] attribute) => string.Join('`', attribute);

	public static string Attribute(DBRef dbref, string[] attribute)
		=> $"attribute:#{dbref.Number}:{AttributePath(attribute)}";

	// The lazy- prefix keeps this from colliding with Attribute, which caches a different element
	// type under the same identity. Matches the inheritance query pair.
	public static string LazyAttribute(DBRef dbref, string[] attribute)
		=> $"lazy-attribute:#{dbref.Number}:{AttributePath(attribute)}";

	public static string AttributeWithInheritance(DBRef dbref, string[] attribute, bool checkParent)
		=> $"attribute-inheritance:#{dbref.Number}:{AttributePath(attribute)}:{checkParent}";

	public static string LazyAttributeWithInheritance(DBRef dbref, string[] attribute, bool checkParent)
		=> $"lazy-attribute-inheritance:#{dbref.Number}:{AttributePath(attribute)}:{checkParent}";

	/// <summary>The object's $-command attributes, with their patterns compiled.</summary>
	public static string Commands(DBRef dbref) => $"commands:#{dbref.Number}";

	/// <summary>The object's ^-listen attributes, with their patterns compiled.</summary>
	public static string Listens(DBRef dbref) => $"listens:#{dbref.Number}";

	// Keyed by dbref NUMBER only: an ancestor is named by number everywhere it is consulted.
	public static string AncestorCommands(int number) => $"ancestor-commands:#{number}";
	public static string AncestorListens(int number) => $"ancestor-listens:#{number}";

	/// <summary>
	/// Tag on the attribute reads that consult exactly one object — <see cref="Attribute"/> and
	/// <see cref="LazyAttribute"/>. Per object, for the same reason <see cref="ContentsTag"/> is per
	/// container: attribute writes are the hot path, and one game-wide tag made every <c>&amp;ATTR</c>
	/// anywhere drop every object's cached attributes.
	///
	/// <para>The inherited reads do NOT get this. They answer "what does this object see, counting its
	/// parents and zones", so a write to any object in that chain changes the answer — including a
	/// write that makes a nearer ancestor shadow a further one. The chain a read walked is not in its
	/// result (a not-found answer is an empty stream), so there is nothing to scope by until the
	/// providers project the objects they visited; they keep
	/// <see cref="CacheTags.InheritedAttributes"/>, which over-expires rather than going stale.</para>
	/// </summary>
	public static string AttributesTag(int number) => $"object-attributes:#{number}";

	/// <summary>
	/// Every cached entry a write to <paramref name="attribute"/> on <paramref name="dbref"/> makes
	/// stale, by key.
	///
	/// <para>Every prefix of the path, not only the path itself: setting <c>FOO`BAR</c> changes
	/// <c>FOO</c> too — it gains a leaf, and the branch flag with it.</para>
	/// </summary>
	public static string[] AttributesTouchedBy(DBRef dbref, string[] attribute)
	{
		var keys = new List<string>((attribute.Length * 6) + 4);

		for (var length = 1; length <= attribute.Length; length++)
		{
			var prefix = attribute[..length];
			keys.Add(Attribute(dbref, prefix));
			keys.Add(LazyAttribute(dbref, prefix));
			keys.Add(AttributeWithInheritance(dbref, prefix, true));
			keys.Add(AttributeWithInheritance(dbref, prefix, false));
			keys.Add(LazyAttributeWithInheritance(dbref, prefix, true));
			keys.Add(LazyAttributeWithInheritance(dbref, prefix, false));
		}

		// The $-command and ^-listen sets are derived from the object's attributes, so any attribute
		// write can change them — the attribute written is not necessarily the one carrying a pattern,
		// since a flag change alone can add or remove one.
		keys.Add(Commands(dbref));
		keys.Add(Listens(dbref));
		keys.Add(AncestorCommands(dbref.Number));
		keys.Add(AncestorListens(dbref.Number));

		return [.. keys];
	}
}
