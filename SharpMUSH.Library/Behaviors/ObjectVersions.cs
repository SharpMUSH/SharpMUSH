using System.Collections.Concurrent;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// A monotonic version per object number, bumped by every invalidation of that object's key.
/// </summary>
/// <remarks>
/// <para>
/// A key removal during a read says nothing about the entry the read stores afterwards, so a read
/// that queried before a write committed and stored after the write's invalidation would keep a
/// pre-write object for the entry's whole lifetime. The caching behaviour reads the version before
/// its factory runs and again after the store; if it moved, the entry it just stored is removed.
/// Tagged entries do not need this: FusionCache stamps a foreground factory's entry at factory
/// start, so a tag removed mid-read already expires the result. One engine process is assumed;
/// more than one node would need this counter in the shared cache.
/// </para>
/// <para>
/// The table holds one entry per object number ever written, and dbrefs are recycled, so it is
/// bounded by the highest dbref the database has allocated rather than by write volume: a few
/// tens of bytes per object, the same order as one cached object node. A recycled dbref keeps
/// its old count, which is fine, since only movement is compared.
/// </remarks>
public sealed class ObjectVersions
{
	private readonly ConcurrentDictionary<int, long> _versions = new();

	public long Of(int number) => _versions.GetValueOrDefault(number);

	public void Bump(int number) => _versions.AddOrUpdate(number, 1, static (_, version) => version + 1);
}
