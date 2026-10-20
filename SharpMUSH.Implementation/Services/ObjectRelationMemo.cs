using System.Collections.Concurrent;
using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The last answer to an object's grants, owner, location, parent and flags, kept per object until that object is
/// written. Every permission check reads these, often several times over (<c>loc()</c> as a mortal asks for
/// the grants of both sides five times, the owner of each and the location of each), and each read
/// through the cache costs a tagged lookup: 40-60 µs per call for <c>loc()</c>, <c>room()</c> or
/// <c>get()</c> on another player, against 6 µs for <c>name()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Coherent by version, as <c>ExecutorDebugFlags</c> is (<c>docs/design/engine-data-trunk.md</c> §7). The
/// object's <see cref="ObjectVersions"/> entry moves before and after every write that removes its key,
/// which every write to its grants (a role or override on it), owner (<c>@chown</c>) or location (a move)
/// does. Grants also move with <see cref="CacheTags.Grants"/>: a role's permissions, or an account's roles
/// or link. The version is read before the answer, so a write landing while it is read leaves an entry
/// that the next read refuses.
/// </para>
/// <para>
/// Flags move with the object's version too (<c>@set</c> and every other flag write removes its key), and
/// with <see cref="CacheTags.FlagList"/>: a flag defined, renamed, aliased, disabled or deleted. A
/// <c>u()</c> call reads its object's flags three to five times (HALT, ORPHAN, and the flags
/// <c>controls()</c> asks of both sides), each an LMDB read of about a microsecond.
/// </para>
/// <para>
/// Owner and location are kept as the object id they name, with the object that id resolved to and the
/// version that object had then. While neither version has moved the kept object is the answer; once the
/// named object's moves, the id is resolved through the object node cache again, as the cached relation
/// queries are. An id whose object is gone or was recycled resolves to nothing, and the read falls through
/// to the query: that is the case the queries' embedded-object tag covers (deleting an object severs edges
/// on objects nobody wrote, and removes the deleted object's key, which moves its version). Resolving the
/// owner through the node cache was most of what <c>controls()</c> cost once flags were kept: <c>Owns</c>
/// and <c>Inheritable</c> each read an owner.
/// </para>
/// <para>
/// One entry per object number asked about, so each table is bounded by the highest dbref, as
/// <see cref="ObjectVersions"/> is.
/// </para>
/// </remarks>
public sealed class ObjectRelationMemo(IMediator mediator, ObjectVersions versions)
{
	// A hit hands back the completed task stored with the answer, so it allocates nothing.
	private sealed record GrantsEntry(long Version, long GrantsTable, bool IsPlayer, Task<ObjectGrants> Grants);

	/// <summary><paramref name="Value"/> is a completed task for what <paramref name="Ref"/> resolved to at
	/// <paramref name="RefVersion"/>, read before the resolve; null until the id has been resolved from the memo once.</summary>
	private sealed record RefEntry(long Version, DBRef Ref, long RefVersion, Task? Value);

	private sealed record FlagsEntry(long Version, long FlagTable, string Type, SharpObjectFlag[] Flags);

	private readonly ConcurrentDictionary<int, GrantsEntry> _grants = new();
	private readonly ConcurrentDictionary<int, RefEntry> _owners = new();
	private readonly ConcurrentDictionary<int, RefEntry> _locations = new();
	private readonly ConcurrentDictionary<int, RefEntry> _parents = new();

	/// <summary>The version at which each object was read with no parent; an entry in <see cref="_parents"/> holds one it had.</summary>
	private readonly ConcurrentDictionary<int, long> _parentless = new();

	private static readonly Task<AnyOptionalSharpObject> NoParent = Task.FromResult(new AnyOptionalSharpObject(new None()));
	private readonly ConcurrentDictionary<int, FlagsEntry> _flags = new();

	public Task<ObjectGrants> GrantsAsync(int number, bool isPlayer, Func<CancellationToken, Task<ObjectGrants>> read,
		CancellationToken cancellationToken)
	{
		var version = versions.Of(number);
		var grantsTable = versions.OfTag(CacheTags.Grants);
		return _grants.TryGetValue(number, out var known)
				&& known.Version == version && known.GrantsTable == grantsTable && known.IsPlayer == isPlayer
			? known.Grants
			: ReadGrantsAsync(number, isPlayer, version, grantsTable, read, cancellationToken);
	}

	private async Task<ObjectGrants> ReadGrantsAsync(int number, bool isPlayer, long version, long grantsTable,
		Func<CancellationToken, Task<ObjectGrants>> read, CancellationToken cancellationToken)
	{
		var grants = await read(cancellationToken);
		_grants[number] = new GrantsEntry(version, grantsTable, isPlayer, Task.FromResult(grants));
		return grants;
	}

	/// <summary>The flags <paramref name="read"/> returned for the object's current version; callers never change the array.</summary>
	public SharpObjectFlag[] Flags(int number, string type, Func<SharpObjectFlag[]> read)
	{
		var version = versions.Of(number);
		var flagTable = versions.OfTag(CacheTags.FlagList);
		if (_flags.TryGetValue(number, out var known)
				&& known.Version == version && known.FlagTable == flagTable && known.Type == type)
		{
			return known.Flags;
		}

		var flags = read();
		_flags[number] = new FlagsEntry(version, flagTable, type, flags);
		return flags;
	}

	public Task<SharpPlayer> OwnerAsync(int number, Func<CancellationToken, Task<SharpPlayer>> read,
		CancellationToken cancellationToken)
		=> RefAsync(_owners, number, read, cancellationToken);

	public Task<AnySharpContainer> LocationAsync(int number, Func<CancellationToken, Task<AnySharpContainer>> read,
		CancellationToken cancellationToken)
		=> RefAsync(_locations, number, read, cancellationToken);

	/// <summary>
	/// Every lock lookup walks the parent chain, the type ancestor's included, so an <c>@lock/interact</c> nobody set
	/// costs each listener of an emit two parent reads. Most objects have no parent, which an owner or location never
	/// is, so that answer is kept apart, for as long as the object's version stands.
	/// </summary>
	public Task<AnyOptionalSharpObject> ParentAsync(int number, Func<CancellationToken, Task<AnyOptionalSharpObject>> read,
		CancellationToken cancellationToken)
	{
		var version = versions.Of(number);
		return _parentless.TryGetValue(number, out var parentlessAt) && parentlessAt == version
			? NoParent
			: RefAsync(_parents, number, ct => ReadParentAsync(number, version, read, ct), cancellationToken);
	}

	private async Task<AnyOptionalSharpObject> ReadParentAsync(int number, long version,
		Func<CancellationToken, Task<AnyOptionalSharpObject>> read, CancellationToken cancellationToken)
	{
		var parent = await read(cancellationToken);
		if (parent is None) _parentless[number] = version;
		return parent;
	}

	private Task<T> RefAsync<T>(ConcurrentDictionary<int, RefEntry> table, int number,
		Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken) where T : IObjectShaped<T>
	{
		var version = versions.Of(number);
		return table.TryGetValue(number, out var known) && known.Version == version
				&& known.Value is Task<T> kept && versions.Of(known.Ref.Number) == known.RefVersion
			? kept
			: ResolveAsync(table, number, version, known, read, cancellationToken);
	}

	private async Task<T> ResolveAsync<T>(ConcurrentDictionary<int, RefEntry> table, int number, long version,
		RefEntry? known, Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken) where T : IObjectShaped<T>
	{
		if (known is not null && known.Version == version)
		{
			var refVersion = versions.Of(known.Ref.Number);
			if (T.TryFromNode(await mediator.Send(new GetObjectNodeQuery(known.Ref), cancellationToken), out var found))
			{
				table[number] = known with { RefVersion = refVersion, Value = Task.FromResult(found) };
				return found;
			}
		}

		var value = await read(cancellationToken);
		if (T.RefOf(value) is { } reference)
		{
			// The read resolved the object before its version could be read, so it is not kept yet.
			table[number] = new RefEntry(version, reference, 0, null);
		}

		return value;
	}
}
