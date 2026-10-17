using System.Collections.Concurrent;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Process-local monotonic invalidation counts and epochs for object keys and tags.
/// Entries retain the factory-start epoch so even a late tag-marker store cannot revive a stale fill.
/// </summary>
public sealed class ObjectVersions
{
	private readonly record struct Stamp(long Count, long LastChange);
	private readonly ConcurrentDictionary<int, Stamp> _versions = new();
	private readonly ConcurrentDictionary<string, Stamp> _tagVersions = new(StringComparer.Ordinal);
	private long _epoch;

	public long Epoch => Volatile.Read(ref _epoch);
	public long Of(int number) => _versions.GetValueOrDefault(number).Count;
	public long OfTag(string tag) => _tagVersions.GetValueOrDefault(tag).Count;

	public void Bump(int number)
	{
		var epoch = Interlocked.Increment(ref _epoch);
		_versions.AddOrUpdate(number, new Stamp(1, epoch), (_, previous) =>
			new Stamp(previous.Count + 1, Math.Max(previous.LastChange, epoch)));
	}

	public void BumpTag(string tag)
	{
		var epoch = Interlocked.Increment(ref _epoch);
		_tagVersions.AddOrUpdate(tag, new Stamp(1, epoch), (_, previous) =>
			new Stamp(previous.Count + 1, Math.Max(previous.LastChange, epoch)));
	}

	/// <summary>Tests the effective tag against both explicit tag removal and object-key removal.</summary>
	public bool ChangedAfter(string tag, long epoch)
	{
		if (_tagVersions.GetValueOrDefault(tag).LastChange > epoch) return true;
		return tag.StartsWith("obj:#", StringComparison.Ordinal)
			&& int.TryParse(tag.AsSpan("obj:#".Length), out var number)
			&& _versions.GetValueOrDefault(number).LastChange > epoch;
	}
}
