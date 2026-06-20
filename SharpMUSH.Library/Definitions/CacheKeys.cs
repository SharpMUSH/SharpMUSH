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

	public static string Contents(int number) => $"object-contents:#{number}";
	public static string Contents(DBRef dbref) => Contents(dbref.Number);
}
