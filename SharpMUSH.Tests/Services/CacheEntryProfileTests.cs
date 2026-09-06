using Microsoft.Extensions.Caching.Memory;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using System.Reflection;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Pins the one cache setting that decides correctness rather than speed: a tag-invalidated entry
/// must never be served stale. FusionCache's <c>RemoveByTag</c> is an expire, so an entry with
/// fail-safe on would come back as a fallback while the database is slow or down - a room showing
/// its pre-move contents, which is what the per-container contents tag (#854) exists to prevent.
/// These run against a real in-memory FusionCache, no database, so they test FusionCache's actual
/// behaviour under our options rather than our reading of its documentation.
/// </summary>
public class CacheEntryProfileTests
{
	private static FusionCache NewCache() => new(new FusionCacheOptions
	{
		DefaultEntryOptions = CacheEntryProfiles.Object,
	}, new MemoryCache(new MemoryCacheOptions
	{
		SizeLimit = CacheEntryProfiles.MemoryCacheSizeLimit,
	}));

	[Test]
	public async Task ATaggedEntryIsNotServedStaleAfterItsTagIsRemoved()
	{
		using var cache = NewCache();
		await cache.SetAsync("contents:#1", "before the move", CacheEntryProfiles.Tagged, tags: ["contents-tag:#1"]);
		await cache.RemoveByTagAsync("contents-tag:#1");

		await Assert.That(async () => await cache.GetOrSetAsync<string>("contents:#1",
				(_, _) => throw new InvalidOperationException("database unavailable"),
				options: CacheEntryProfiles.Tagged, tags: ["contents-tag:#1"]))
			.Throws<InvalidOperationException>()
			.Because("after a tag invalidation the only acceptable answers are the database's or an error");
	}

	[Test]
	public async Task AnObjectEntryIsNotServedStaleAfterItsKeyIsRemoved()
	{
		using var cache = NewCache();
		await cache.SetAsync("object:#1", "before the write", CacheEntryProfiles.Object);
		await cache.RemoveAsync("object:#1");

		await Assert.That(async () => await cache.GetOrSetAsync<string>("object:#1",
				(_, _) => throw new InvalidOperationException("database unavailable"),
				options: CacheEntryProfiles.Object))
			.Throws<InvalidOperationException>()
			.Because("every write removes its key, so a removed entry must never resurface as a fallback");
	}

	[Test]
	public async Task AnObjectEntryIsServedStaleWhenItAgesOutAndTheDatabaseFails()
	{
		using var cache = NewCache();
		var shortLived = CacheEntryProfiles.Object.Duplicate(TimeSpan.FromMilliseconds(50));
		shortLived.JitterMaxDuration = TimeSpan.Zero;
		await cache.SetAsync("object:#1", "last known", shortLived);
		await Task.Delay(200);

		var served = await cache.GetOrSetAsync<string>("object:#1",
			(_, _) => throw new InvalidOperationException("database unavailable"),
			options: shortLived);

		await Assert.That(served).IsEqualTo("last known")
			.Because("no write happened - the entry merely aged out - so the last known object beats an error on every command");
	}

	[Test]
	public async Task OnlyTheObjectProfileHasFailSafe()
	{
		await Assert.That(CacheEntryProfiles.Object.IsFailSafeEnabled).IsTrue();
		await Assert.That(CacheEntryProfiles.Tagged.IsFailSafeEnabled).IsFalse();
		await Assert.That(CacheEntryProfiles.Scan.IsFailSafeEnabled).IsFalse();
	}

	/// <summary>
	/// A foreground factory's entry is stamped when the factory starts, so a tag removed mid-read
	/// expires the result. An entry stored by a background completion - an eager refresh, or a
	/// timed-out factory allowed to finish - is stamped when stored, after the tag, and survives it
	/// holding pre-write data. Hence no eager refresh where tags invalidate, and no background
	/// completion anywhere.
	/// </summary>
	[Test]
	public async Task NothingATagCanInvalidateIsEverStoredFromABackgroundFactory()
	{
		await Assert.That(CacheEntryProfiles.Tagged.EagerRefreshThreshold).IsNull();
		await Assert.That(CacheEntryProfiles.Scan.EagerRefreshThreshold).IsNull();
		await Assert.That(CacheEntryProfiles.Object.AllowTimedOutFactoryBackgroundCompletion).IsFalse();
		await Assert.That(CacheEntryProfiles.Tagged.AllowTimedOutFactoryBackgroundCompletion).IsFalse();
		await Assert.That(CacheEntryProfiles.Scan.AllowTimedOutFactoryBackgroundCompletion).IsFalse();
	}

	[Test]
	public async Task EveryProfileCarriesASizeSoTheBoundedMemoryCacheAcceptsIt()
	{
		await Assert.That(CacheEntryProfiles.Object.Size).IsEqualTo(1);
		await Assert.That(CacheEntryProfiles.Tagged.Size).IsEqualTo(1);
		await Assert.That(CacheEntryProfiles.Scan.Size).IsEqualTo(1);
	}

	[Test]
	public async Task TheProfileFollowsFromTheTags()
	{
		var room = new DBRef(2);

		await Assert.That(((ICacheable)new GetObjectNodeByNumberQuery(1)).Profile).IsEqualTo(CacheEntryProfile.Object)
			.Because("an object node is invalidated by key alone");
		await Assert.That(((ICacheable)new GetContentsQuery(room)).Profile).IsEqualTo(CacheEntryProfile.Tagged)
			.Because("contents carry the per-container tag a move invalidates by");
		await Assert.That(((ICacheable)new GetLocationQuery(room)).Profile).IsEqualTo(CacheEntryProfile.Tagged);
		await Assert.That(((ICacheable)new GetAttributeQuery(room, ["DESCRIBE"])).Profile).IsEqualTo(CacheEntryProfile.Tagged);
		await Assert.That(((ICacheable)new GetObjectsByZoneQuery(room)).Profile).IsEqualTo(CacheEntryProfile.Scan);
	}

	[Test]
	public async Task NoCacheableQueryDeclaresTagsAndAsksForFailSafe()
	{
		// The derivation on ICacheable is the rule; this catches a query that overrides Profile to
		// Object while carrying tags, which is the one combination that can serve stale after a write.
		var offenders = typeof(ICacheable).Assembly.GetTypes()
			.Where(t => typeof(ICacheable).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
			.Select(t => t.GetProperty(nameof(ICacheable.Profile)))
			.Where(p => p is not null && p.DeclaringType != typeof(ICacheable))
			.Select(p => p!.DeclaringType!)
			.Where(t => TryInstantiate(t) is { } q && q.CacheTags.Length > 0 && q.Profile == CacheEntryProfile.Object)
			.Select(t => t.Name)
			.ToArray();

		await Assert.That(offenders).IsEmpty();
	}

	private static ICacheable? TryInstantiate(Type type)
	{
		try
		{
			var ctor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
			var args = ctor.GetParameters()
				.Select(p => p.ParameterType switch
				{
					var t when t == typeof(DBRef) => (object)new DBRef(2),
					var t when t == typeof(string) => "x",
					var t when t == typeof(string[]) => new[] { "x" },
					var t when t == typeof(int) => 1,
					var t when t == typeof(bool) => true,
					var t when t == typeof(OneOf.OneOf<DBRef, AnySharpContainer>) => (object)(OneOf.OneOf<DBRef, AnySharpContainer>)new DBRef(2),
					var t when t == typeof(OneOf.OneOf<DBRef, AnySharpObject>) => (object)(OneOf.OneOf<DBRef, AnySharpObject>)new DBRef(2),
					var t => t.IsValueType ? Activator.CreateInstance(t) : null,
				})
				.ToArray();
			return ctor.Invoke(args) as ICacheable;
		}
		catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException
			or TargetInvocationException or ArgumentException or NotSupportedException)
		{
			// Not every ICacheable has a constructor this can satisfy; those are simply not checked.
			return null;
		}
	}
}
