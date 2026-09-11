using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Unit-level coverage of <see cref="LightningDatabase.GetFilteredObjectsAsync"/> for the two things the
/// integration-level <c>ObjectSearchFilterPushdownTests</c> does not exercise directly against this
/// provider: <c>MinDbRef</c>/<c>MaxDbRef</c> bounding the scan's start and stop points, and <c>Skip</c>/
/// <c>Limit</c> slicing the matches in ascending dbref order (the order <see cref="Store.Tables.Obj"/>'s
/// big-endian dbref keys naturally stream in).
/// </summary>
public class FilteredSearchTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}
	}

	[Test]
	public async Task MinAndMaxDbRefBoundTheScanInclusively()
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Expect<AnySharpObject>().AsContainer;
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

		var first = await _db.CreateThingAsync("RangeBoundsA", room, god, room);
		var second = await _db.CreateThingAsync("RangeBoundsB", room, god, room);
		var third = await _db.CreateThingAsync("RangeBoundsC", room, god, room);

		// A range pinned to exactly the middle dbref returns only that object — proves the scan both
		// starts at MinDbRef (not before it) and stops at MaxDbRef (not after it).
		var exact = await _db.GetFilteredObjectsAsync(new ObjectSearchFilter
		{
			MinDbRef = second.Number,
			MaxDbRef = second.Number
		}).ToListAsync();

		await Assert.That(exact.Select(o => o.DBRef.Number)).IsEquivalentTo([second.Number]);

		// A range spanning all three, further scoped by name so seed objects outside the created trio
		// (which also fall inside [first, third] once the seed's own low dbrefs are excluded by
		// MinDbRef) can't sneak in, must return exactly the three created objects.
		var spanning = await _db.GetFilteredObjectsAsync(new ObjectSearchFilter
		{
			MinDbRef = first.Number,
			MaxDbRef = third.Number,
			NamePattern = "RangeBounds"
		}).ToListAsync();

		await Assert.That(spanning.Select(o => o.DBRef.Number))
			.IsEquivalentTo([first.Number, second.Number, third.Number]);
	}

	[Test]
	public async Task SkipAndLimitPageThroughMatchesInAscendingDbRefOrder()
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Expect<AnySharpObject>().AsContainer;
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

		var created = new List<DBRef>();
		for (var i = 0; i < 5; i++)
		{
			created.Add(await _db.CreateThingAsync($"PagingWidget{i}", room, god, room));
		}

		// Skip the first match, take the next two — must be created[1] and created[2], in that order,
		// not an unordered subset of the remaining four.
		var page = await _db.GetFilteredObjectsAsync(new ObjectSearchFilter
		{
			NamePattern = "PagingWidget",
			Skip = 1,
			Limit = 2
		}).ToListAsync();

		await Assert.That(page.Select(o => o.DBRef.Number))
			.IsEquivalentTo([created[1].Number, created[2].Number], CollectionOrdering.Matching);

		// Skip past every match: nothing comes back rather than wrapping or throwing.
		var exhausted = await _db.GetFilteredObjectsAsync(new ObjectSearchFilter
		{
			NamePattern = "PagingWidget",
			Skip = 5
		}).ToListAsync();

		await Assert.That(exhausted).IsEmpty();
	}
}
