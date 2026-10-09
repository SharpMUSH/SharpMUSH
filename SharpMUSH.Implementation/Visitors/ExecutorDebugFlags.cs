using System.Collections.Concurrent;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Whether an executor has the DEBUG flag, which every function call asks. Reading an object's flags
/// opens a database read each time, about a quarter of what a call like <c>ansi(hr,text)</c> costs, so
/// the answer is kept per object until that object is written (<see cref="ObjectVersions"/>, bumped by
/// <c>@set</c> and every other write that invalidates the object) or the flag table changes.
/// </summary>
/// <remarks>
/// One entry per object number asked about, so it is bounded by the database's highest dbref, as
/// <see cref="ObjectVersions"/> is. The creation time tells a recycled dbref from the object that had it.
/// A write landing while the flags are read moves the version past the one stored, so the next call
/// reads again.
/// </remarks>
public sealed class ExecutorDebugFlags(ObjectVersions versions)
{
	private sealed record Entry(long CreationTime, long Version, long FlagTable, bool Debug);

	private readonly ConcurrentDictionary<int, Entry> _entries = new();

	public async ValueTask<bool> IsDebugging(AnySharpObject executor)
	{
		var obj = executor.Object();
		var number = obj.DBRef.Number;
		var version = versions.Of(number);
		var flagTable = versions.OfTag(CacheTags.FlagList);

		if (_entries.TryGetValue(number, out var known)
			&& known.Version == version && known.FlagTable == flagTable && known.CreationTime == obj.CreationTime)
		{
			return known.Debug;
		}

		var debug = await executor.HasFlag("DEBUG");
		_entries[number] = new Entry(obj.CreationTime, version, flagTable, debug);
		return debug;
	}
}
