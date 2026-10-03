namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Running totals of the attribute reads this provider instance has made, so a test or a benchmark can
/// see what a call actually touched rather than infer it from cache hits: how many <c>attr.val</c> bodies
/// were read, how many <c>attr.meta</c> rows were looked at (point reads and rows a range stepped over),
/// and how many root..leaf path walks ran. Per instance, never static: several hosts share a process.
/// Counting is one interlocked increment per row, cheap beside the read it counts.
/// </summary>
internal sealed class AttributeReadStats
{
	private long _valueReads;
	private long _metaRowReads;
	private long _pathWalks;

	/// <summary><c>attr.val</c> rows read.</summary>
	public long ValueReads => Interlocked.Read(ref _valueReads);

	/// <summary><c>attr.meta</c> rows read: point lookups plus every row a range or seek stepped onto.</summary>
	public long MetaRowReads => Interlocked.Read(ref _metaRowReads);

	/// <summary>Root..leaf path walks (<c>ReadPathPrefixes</c>), one per object a lookup resolved against.</summary>
	public long PathWalks => Interlocked.Read(ref _pathWalks);

	internal void ValueRead() => Interlocked.Increment(ref _valueReads);

	internal void MetaRowRead() => Interlocked.Increment(ref _metaRowReads);

	internal void PathWalk() => Interlocked.Increment(ref _pathWalks);
}

public partial class LightningDatabase
{
	/// <summary>What this instance has read from the attribute tables; see <see cref="AttributeReadStats"/>.</summary>
	internal AttributeReadStats ReadStats { get; } = new();
}
