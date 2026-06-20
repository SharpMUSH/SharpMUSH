using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Proves the cache read/write key DISAGREEMENT behind the loc()-stale-after-move bug.
///
/// READ side  (QueryCachingBehavior caches under this): GetObjectNodeQuery.CacheKey => $"object:{DBRef}".
/// WRITE side (CacheInvalidationBehavior removes these): every mutating command, e.g.
///            MoveObjectCommand.CacheKeys => $"object:{Target.Object().DBRef}".
///
/// DBRef.ToString() is "#N" when CreationMilliseconds is null and "#N:ms" otherwise, and a loaded
/// object's DBRef is built as `new DBRef(int.Parse(obj.Key), time)` (always WITH the timestamp). So a
/// bare "#N" reference caches under "object:#N" while every invalidation removes the FULL "object:#N:ms" —
/// the bare entry is never cleared.
/// </summary>
[NotInParallel]
public class ObjectCacheKeyConsistencyTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private const long SampleCreation = 1781000000000L;

	// ── READ side ────────────────────────────────────────────────────────────────────────────────
	[Test]
	public async ValueTask ReadCacheKey_IsTimestampSensitive_BareVsFull()
	{
		var bareKey = new GetObjectNodeQuery(new DBRef(10)).CacheKey;
		var fullKey = new GetObjectNodeQuery(new DBRef(10, SampleCreation)).CacheKey;

		await Assert.That(bareKey).IsEqualTo("object:#10");
		await Assert.That(fullKey).IsEqualTo($"object:#10:{SampleCreation}");
		await Assert.That(bareKey).IsNotEqualTo(fullKey)
			.Because("the read cache key changes with the dbref form (bare #N vs full #N:creation)");
	}

	// A softcode "#10" reference (what %#, %!, or a literal #N produce) parses timestamp-less → bare key.
	[Test]
	public async ValueTask BareDbrefString_ParsesTimestampless_ProducingTheBareReadKey()
	{
		var parsed = DBRef.Parse("#10");
		await Assert.That(parsed.CreationMilliseconds).IsNull()
			.Because("a bare '#10' reference has no creation timestamp");
		await Assert.That(new GetObjectNodeQuery(parsed).CacheKey).IsEqualTo("object:#10");
	}

	// ── WRITE side vs READ side ──────────────────────────────────────────────────────────────────
	// Mirrors MoveObjectCommand.CacheKeys ($"object:{Target.Object().DBRef}"); a loaded object's DBRef
	// is full-form, so the invalidation key matches a FULL read but never a BARE read.
	[Test]
	public async ValueTask MoveInvalidationKey_MatchesFullRead_ButMissesBareRead()
	{
		var loadedObjectDbref = new DBRef(10, SampleCreation);   // == Target.Object().DBRef (always full)
		var invalidationKey = $"object:{loadedObjectDbref}";

		var fullReadKey = new GetObjectNodeQuery(loadedObjectDbref).CacheKey;
		var bareReadKey = new GetObjectNodeQuery(new DBRef(10)).CacheKey;

		await Assert.That(invalidationKey).IsEqualTo(fullReadKey)
			.Because("a move invalidates exactly the full-objid read key");
		await Assert.That(invalidationKey).IsNotEqualTo(bareReadKey)
			.Because("a move NEVER invalidates the bare #N read key → loc(#10) stays cached and goes stale");
	}

	// ── Linchpin (integration): a real loaded object's DBRef is full-form ─────────────────────────
	[Test]
	public async ValueTask LoadedObjectDbref_CarriesCreationTimestamp_SoInvalidationIsFullForm()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CacheKeyLinchpin");

		await Assert.That(thing.CreationMilliseconds).IsNotNull()
			.Because("a real object's DBRef carries its creation time, so every mutation's invalidation key is the FULL objid");

		var invalidationKey = $"object:{thing}";                                  // object:#N:ms (write)
		var bareReadKey = new GetObjectNodeQuery(new DBRef(thing.Number)).CacheKey; // object:#N    (loc(#N) read)
		await Assert.That(invalidationKey).IsNotEqualTo(bareReadKey)
			.Because("the move invalidates object:{full-objid} while loc(#N) cached object:#N — the disagreement, with a real object");
	}
}
