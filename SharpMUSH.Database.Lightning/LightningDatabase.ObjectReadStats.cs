using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Running totals of the full object hydrations this provider instance has made (an <c>obj</c> row decoded
/// into a Library model, with its locks and lazy relations), so a test or a benchmark can see how many times
/// a call built an object rather than infer it from cache hits. <see cref="AttributeReadStats"/> is the
/// attribute-side counterpart. Per instance, never static: several hosts share a process.
/// <para>
/// A host shared by tests running in parallel hydrates objects all the time, so the per-object count is
/// kept only for the numbers a caller asked to <see cref="Watch"/>: a test watches the objects it created
/// and reads their counts, untouched by what other tests load. With nothing watched the cost is one
/// interlocked increment and an emptiness check per hydration.
/// </para>
/// </summary>
internal sealed class ObjectReadStats
{
	private long _hydrations;
	private readonly ConcurrentDictionary<long, StrongBox<long>> _watched = new();

	/// <summary>Full object hydrations, every object.</summary>
	public long Hydrations => Interlocked.Read(ref _hydrations);

	/// <summary>Starts counting the hydrations of <paramref name="dbref"/> (see <see cref="HydrationsOf"/>).</summary>
	public void Watch(long dbref) => _watched.TryAdd(dbref, new StrongBox<long>());

	/// <summary>Full hydrations of <paramref name="dbref"/> since it was watched; zero for an unwatched object.</summary>
	public long HydrationsOf(long dbref)
		=> _watched.TryGetValue(dbref, out var count) ? Interlocked.Read(ref count.Value) : 0;

	internal void Hydrated(long dbref)
	{
		Interlocked.Increment(ref _hydrations);
		if (!_watched.IsEmpty && _watched.TryGetValue(dbref, out var count))
		{
			Interlocked.Increment(ref count.Value);
		}
	}
}

public partial class LightningDatabase
{
	/// <summary>What this instance has hydrated from the object table; see <see cref="ObjectReadStats"/>.</summary>
	internal ObjectReadStats ObjectStats { get; } = new();
}
