using System.Collections.Immutable;

namespace SharpMUSH.Library.Behaviors;

/// <summary>An immutable freshness certificate captured by the factory that produced the value.</summary>
public sealed class CachedValue<T>(T value, long epoch, IEnumerable<string> tags)
{
	public T Value { get; } = value;
	public long Epoch { get; } = epoch;
	public ImmutableArray<string> Tags { get; } = [.. tags.Distinct(StringComparer.Ordinal)];

	public bool IsCurrent(ObjectVersions versions) => Tags.All(tag => !versions.ChangedAfter(tag, Epoch));
}
