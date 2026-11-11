using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Clusters;

/// <summary>
/// The world's clusters. RhostMUSH spreads one cluster's attributes over several objects; an import merges each
/// cluster into one object, and this table keeps the numbers the other objects had, so softcode that still names
/// them reaches the object they were merged into.
/// </summary>
/// <param name="Members">Each merged object's objid, and the numbers of the objects merged into it.</param>
public sealed record ObjectClusterTable(int Version, IReadOnlyDictionary<string, IReadOnlyList<int>> Members)
{
	public static ObjectClusterTable Empty => new(1, new Dictionary<string, IReadOnlyList<int>>());
}

/// <summary>Reads <see cref="ObjectClusterTable"/> through the Mediator, and answers what a cluster is.</summary>
public static class ObjectClusters
{
	/// <summary>The server data key the table is kept under.</summary>
	public const string Key = "sharpmush.clusters.v1";

	/// <summary>
	/// The object a cluster member's number was merged into: <paramref name="number"/> when no object holds it and
	/// it was one of a cluster's members, and that cluster's object still exists.
	/// </summary>
	public static async ValueTask<Found<DBRef>> MergedIntoAsync(IMediator mediator, DBRef number, CancellationToken ct = default)
	{
		// Only a bare number below which no seeded object stands can have been a member: an objid names one object.
		if (number.Number <= 2 || number.CreationMilliseconds is not null)
		{
			return new NotFound();
		}

		var table = await mediator.Send(new GetObjectClustersQuery(), ct);
		foreach (var (merged, members) in table.Members)
		{
			if (!members.Contains(number.Number))
			{
				continue;
			}

			if (await mediator.Send(new GetObjectNodeQuery(number), ct) is AnySharpObject)
			{
				return new NotFound();
			}

			return await LiveAsync(mediator, merged, ct) is { } live ? live : new NotFound();
		}

		return new NotFound();
	}

	/// <summary>
	/// The numbers that make up <paramref name="target"/>'s cluster, its own first, when it is one; a member number
	/// a new object has since taken is left out.
	/// </summary>
	public static async ValueTask<Found<IReadOnlyList<DBRef>>> ClusterOfAsync(IMediator mediator, AnySharpObject target,
		CancellationToken ct = default)
	{
		var self = target.Object().DBRef;
		var table = await mediator.Send(new GetObjectClustersQuery(), ct);
		foreach (var (merged, members) in table.Members)
		{
			if (!DBRef.TryParse(merged, out var stored) || stored is not { } reference || !self.Matches(reference))
			{
				continue;
			}

			var numbers = new List<DBRef> { new(self.Number) };
			foreach (var member in members.Where(member => member != self.Number))
			{
				if (await mediator.Send(new GetObjectNodeQuery(new DBRef(member)), ct) is not AnySharpObject)
				{
					numbers.Add(new DBRef(member));
				}
			}

			return numbers;
		}

		return new NotFound();
	}

	/// <summary>Whether <paramref name="target"/> is a cluster's object.</summary>
	public static async ValueTask<bool> IsClusterAsync(IMediator mediator, AnySharpObject target, CancellationToken ct = default)
		=> await ClusterOfAsync(mediator, target, ct) is IReadOnlyList<DBRef>;

	/// <summary>
	/// Records <paramref name="merged"/> as a cluster whose other objects had <paramref name="members"/>. An existing
	/// entry for the same object takes the new numbers as well; entries whose object is gone are dropped.
	/// </summary>
	public static async ValueTask AddAsync(IMediator mediator, DBRef merged, IEnumerable<int> members, CancellationToken ct = default)
	{
		// One read-change-write at a time, so two additions cannot each keep only their own. A gate per process rather
		// than per world only ever makes another world's addition wait its turn.
		await AddGate.WaitAsync(ct);
		try
		{
			await AddLockedAsync(mediator, merged, members, ct);
		}
		finally
		{
			AddGate.Release();
		}
	}

	private static readonly SemaphoreSlim AddGate = new(1, 1);

	private static async ValueTask AddLockedAsync(IMediator mediator, DBRef merged, IEnumerable<int> members, CancellationToken ct)
	{
		var table = await mediator.Send(new GetObjectClustersQuery(), ct);
		var kept = new Dictionary<string, IReadOnlyList<int>>();
		foreach (var (key, numbers) in table.Members)
		{
			if (await LiveAsync(mediator, key, ct) is not null)
			{
				kept[key] = numbers;
			}
		}

		var objid = merged.ToString();
		kept[objid] = (kept.TryGetValue(objid, out var existing) ? existing : [])
			.Concat(members.Where(member => member != merged.Number))
			.Distinct()
			.Order()
			.ToList();
		await mediator.Send(new SetObjectClustersCommand(table with { Members = kept }), ct);
	}

	private static async ValueTask<DBRef?> LiveAsync(IMediator mediator, string stored, CancellationToken ct)
		=> DBRef.TryParse(stored, out var parsed) && parsed is { } reference
			&& await mediator.Send(new GetObjectNodeQuery(reference), ct) is AnySharpObject found
				? found.Object().DBRef
				: null;
}

/// <summary>The world's <see cref="ObjectClusterTable"/>; empty when it has no clusters.</summary>
public record GetObjectClustersQuery : IQuery<ObjectClusterTable>, ICacheable
{
	public string CacheKey => "object-clusters";
	public string[] CacheTags => [Definitions.CacheTags.ObjectClusters];
}

/// <summary>Replaces the world's <see cref="ObjectClusterTable"/>.</summary>
public record SetObjectClustersCommand(ObjectClusterTable Table) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => ["object-clusters"];
	public string[] CacheTags => [Definitions.CacheTags.ObjectClusters];
}
