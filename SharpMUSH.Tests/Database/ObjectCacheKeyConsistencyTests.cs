using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Proves the object-cache read key and write/invalidation key AGREE regardless of dbref form (the
/// number-keying fix for the loc()-stale-after-move bug).
///
/// READ side  — GetObjectNodeByNumberQuery caches under CacheKeys.Object(Number).
/// WRITE side — every mutating command invalidates CacheKeys.Object(...DBRef).
///
/// CacheKeys.Object keys by the dbref NUMBER only, so a bare "#N" reference and a full "#N:creation" objid
/// map to the SAME entry, and a mutation (which knows the number) invalidates exactly the entry a bare-#N
/// read cached under. The objid (recycle) check now lives outside the cache (see
/// DbrefBothFormsResolutionTests.ObjidWithWrongTimestamp_DoesNotResolve_EvenWhenBareIsCached).
/// </summary>
[NotInParallel]
public class ObjectCacheKeyConsistencyTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private const long SampleCreation = 1781000000000L;

	[Test]
	public async ValueTask ObjectKey_IsNumberOnly_BareAndFullUnify()
	{
		var bare = CacheKeys.Object(new DBRef(10));
		var full = CacheKeys.Object(new DBRef(10, SampleCreation));

		await Assert.That(bare).IsEqualTo("object:#10");
		await Assert.That(full).IsEqualTo("object:#10");
		await Assert.That(bare).IsEqualTo(full)
			.Because("the object cache key is the dbref number only — bare #N and full #N:creation unify");
	}

	[Test]
	public async ValueTask ByNumberReadKey_MatchesInvalidationKey()
	{
		var readKey = new GetObjectNodeByNumberQuery(10).CacheKey;          // what a read caches under
		var invalidationKey = CacheKeys.Object(new DBRef(10, SampleCreation)); // what a mutation removes (full dbref)

		await Assert.That(readKey).IsEqualTo("object:#10");
		await Assert.That(invalidationKey).IsEqualTo(readKey)
			.Because("read and write keys now agree on the number — a move invalidates exactly the cached read");
	}

	[Test]
	public async ValueTask BareDbrefString_AndFullObjid_ProduceTheSameKey()
	{
		var fromBare = CacheKeys.Object(DBRef.Parse("#10"));
		var fromFull = CacheKeys.Object(DBRef.Parse($"#10:{SampleCreation}"));

		await Assert.That(fromBare).IsEqualTo("object:#10");
		await Assert.That(fromFull).IsEqualTo("object:#10")
			.Because("a full objid and a bare reference to the same number share one cache entry");
	}

	[Test]
	public async ValueTask LoadedObjectDbref_CarriesTimestamp_ButKeyDropsIt_SoInvalidationMatchesBareRead()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CacheKeyLinchpin");

		await Assert.That(thing.CreationMilliseconds).IsNotNull()
			.Because("a real object's DBRef carries its creation time");

		var invalidationKey = CacheKeys.Object(thing);                  // write side (full-form dbref)
		var bareReadKey = CacheKeys.Object(new DBRef(thing.Number));    // read side (loc(#N))
		await Assert.That(invalidationKey).IsEqualTo(bareReadKey)
			.Because("CacheKeys.Object drops the timestamp, so a move invalidates the same entry loc(#N) caches");
	}
}
