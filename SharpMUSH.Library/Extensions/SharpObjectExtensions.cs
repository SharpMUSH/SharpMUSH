using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class SharpObjectExtensions
{
	/// <summary>
	/// Check if an object has the NO_WARN flag set
	/// </summary>
	public static async Task<bool> HasNoWarnFlagAsync(this SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToListAsync();
		return flags.Any(x => x.Name == "NO_WARN");
	}

	/// <summary>
	/// PennMUSH's <c>AreQuiet(&lt;player&gt;, &lt;thing&gt;)</c> (<c>hdrs/dbdefs.h:198</c>): the player
	/// is QUIET, or the thing is QUIET and the player owns it.
	/// </summary>
	public static async Task<bool> AreQuietAsync(this SharpObject thing, AnySharpObject player)
	{
		if (await player.Object().HasQuietFlagAsync())
		{
			return true;
		}

		return await thing.HasQuietFlagAsync()
			&& (await thing.Owner.WithCancellation(CancellationToken.None)).Object.DBRef == player.Object().DBRef;
	}

	/// <summary>Check if an object has the QUIET flag set.</summary>
	public static async Task<bool> HasQuietFlagAsync(this SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToListAsync();
		return flags.Any(x => x.Name == "QUIET");
	}

	/// <summary>
	/// Check if an object is marked as GOING (being destroyed)
	/// </summary>
	public static async Task<bool> IsGoingAsync(this SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToListAsync();
		return flags.Any(x => x.Name == "GOING");
	}

	/// <summary>
	/// Get the zone chain for an object, walking up the zone hierarchy
	/// </summary>
	/// <param name="obj">The object to get the zone chain for</param>
	/// <param name="maxDepth">Maximum depth to traverse (default 10, -1 for unlimited)</param>
	/// <param name="ct">Cancellation token</param>
	/// <returns>Enumerable of zone objects from immediate zone to root</returns>
	public static async IAsyncEnumerable<AnySharpObject> GetZoneChain(
		this SharpObject obj,
		int maxDepth = 10,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
	{
		var currentZone = await obj.Zone.WithCancellation(ct);
		var depth = 0;

		while (!currentZone.IsNone && (maxDepth < 0 || depth < maxDepth))
		{
			var zone = currentZone.Known;
			yield return zone;

			currentZone = await zone.Object().Zone.WithCancellation(ct);
			depth++;

			// Prevent infinite loops
			if (depth > 100)
			{
				break;
			}
		}
	}

	/// <summary>
	/// Get the zone chain for an AnySharpObject, walking up the zone hierarchy
	/// </summary>
	public static async IAsyncEnumerable<AnySharpObject> GetZoneChain(
		this AnySharpObject obj,
		int maxDepth = 10,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
	{
		await foreach (var zone in obj.Object().GetZoneChain(maxDepth, ct))
		{
			yield return zone;
		}
	}

	/// <summary>
	/// Check if an object is in a zone or any of its parent zones
	/// </summary>
	/// <param name="obj">The object to check</param>
	/// <param name="targetZone">The zone to check for</param>
	/// <param name="checkHierarchy">If true, checks parent zones as well</param>
	/// <param name="ct">Cancellation token</param>
	/// <returns>True if the object is in the zone or zone hierarchy</returns>
	public static async Task<bool> IsInZone(
		this SharpObject obj,
		AnySharpObject targetZone,
		bool checkHierarchy = true,
		CancellationToken ct = default)
	{
		var objectZone = await obj.Zone.WithCancellation(ct);

		if (objectZone.IsNone)
		{
			return false;
		}

		if (objectZone.Known.Object().DBRef.Number == targetZone.Object().DBRef.Number)
		{
			return true;
		}

		if (checkHierarchy)
		{
			await foreach (var zone in obj.GetZoneChain(ct: ct))
			{
				if (zone.Object().DBRef.Number == targetZone.Object().DBRef.Number)
				{
					return true;
				}
			}
		}

		return false;
	}
}
