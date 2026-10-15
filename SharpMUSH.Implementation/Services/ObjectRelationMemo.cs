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
/// The last answer to an object's grants, owner and location, kept per object until that object is
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
/// Owner and location are kept as the object id they name, never the object, and resolved through the
/// object node cache on each read, as the cached relation queries are. An id whose object is gone or was
/// recycled resolves to nothing, and the read falls through to the query: that is the case the queries'
/// embedded-object tag covers (deleting an object severs edges on objects nobody wrote).
/// </para>
/// <para>
/// One entry per object number asked about, so each table is bounded by the highest dbref, as
/// <see cref="ObjectVersions"/> is.
/// </para>
/// </remarks>
public sealed class ObjectRelationMemo(IMediator mediator, ObjectVersions versions)
{
	private sealed record GrantsEntry(long Version, long GrantsTable, bool IsPlayer, ObjectGrants Grants);

	private sealed record RefEntry(long Version, DBRef Ref);

	private readonly ConcurrentDictionary<int, GrantsEntry> _grants = new();
	private readonly ConcurrentDictionary<int, RefEntry> _owners = new();
	private readonly ConcurrentDictionary<int, RefEntry> _locations = new();

	public async Task<ObjectGrants> GrantsAsync(int number, bool isPlayer, Func<CancellationToken, Task<ObjectGrants>> read,
		CancellationToken cancellationToken)
	{
		var version = versions.Of(number);
		var grantsTable = versions.OfTag(CacheTags.Grants);
		if (_grants.TryGetValue(number, out var known)
				&& known.Version == version && known.GrantsTable == grantsTable && known.IsPlayer == isPlayer)
		{
			return known.Grants;
		}

		var grants = await read(cancellationToken);
		_grants[number] = new GrantsEntry(version, grantsTable, isPlayer, grants);
		return grants;
	}

	public Task<SharpPlayer> OwnerAsync(int number, Func<CancellationToken, Task<SharpPlayer>> read,
		CancellationToken cancellationToken)
		=> RefAsync(_owners, number, read, cancellationToken);

	public Task<AnySharpContainer> LocationAsync(int number, Func<CancellationToken, Task<AnySharpContainer>> read,
		CancellationToken cancellationToken)
		=> RefAsync(_locations, number, read, cancellationToken);

	private async Task<T> RefAsync<T>(ConcurrentDictionary<int, RefEntry> table, int number,
		Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken) where T : IObjectShaped<T>
	{
		var version = versions.Of(number);
		if (table.TryGetValue(number, out var known) && known.Version == version
				&& T.TryFromNode(await mediator.Send(new GetObjectNodeQuery(known.Ref), cancellationToken), out var found))
		{
			return found;
		}

		var value = await read(cancellationToken);
		if (T.RefOf(value) is { } reference)
		{
			table[number] = new RefEntry(version, reference);
		}

		return value;
	}
}
