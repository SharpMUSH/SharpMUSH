using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class ExpandedDataTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private string _path = null!;
	private LightningDatabase _db = null!;

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
				// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
				// join by a few milliseconds under load. Leaving the temp directory behind costs
				// disk, not correctness — matches MigrationTests' own cleanup.
			}
		}
	}

	private record ExpandedDataExample(string Word);

	[Test]
	public async Task SetAndGetExpandedObjectDataRoundTrips()
	{
		var data = new ExpandedDataExample("Dog");
		await _db.SetExpandedObjectData("1", "ExpandedDataExample", data);

		var result = await _db.GetExpandedObjectData<ExpandedDataExample>("1", "ExpandedDataExample");

		await Assert.That(result).IsEquivalentTo(data);
	}

	[Test]
	public async Task GetExpandedObjectDataReturnsDefaultWhenAbsent()
	{
		var result = await _db.GetExpandedObjectData<ExpandedDataExample>("1", "NeverSet");

		await Assert.That(result).IsNull();
	}

	private record ExpandedServerDataExample(string Message);

	[Test]
	public async Task SetAndGetExpandedServerDataRoundTrips()
	{
		var data = new ExpandedServerDataExample("Hello, world!");
		await _db.SetExpandedServerData("ExpandedServerDataExample", data);

		var result = await _db.GetExpandedServerData<ExpandedServerDataExample>("ExpandedServerDataExample");

		await Assert.That(result).IsEquivalentTo(data);
	}

	private record PartialUpdateExample(string? Word, string? Verb);

	/// <summary>
	/// SetExpandedObjectData merges: a second write that only sets one property must not wipe a property
	/// the first write set and the second write left null — null means "not set", not "clear this field".
	/// </summary>
	[Test]
	public async Task SecondWriteWithOnePropertyKeepsTheOtherFromTheFirstWrite()
	{
		await _db.SetExpandedObjectData("1", "PartialUpdateExample", new PartialUpdateExample("Dog", "Bark"));
		await _db.SetExpandedObjectData("1", "PartialUpdateExample", new PartialUpdateExample("Cat", null));

		var result = await _db.GetExpandedObjectData<PartialUpdateExample>("1", "PartialUpdateExample");

		await Assert.That(result).IsEquivalentTo(new PartialUpdateExample("Cat", "Bark"));
	}

	/// <summary>
	/// SetExpandedServerData replaces: unlike the object-scoped write, a null property clears the stored
	/// replaces with <c>keepNull</c>, SurrealDB upserts the whole document).
	/// </summary>
	[Test]
	public async Task ServerDataWriteReplacesTheDocumentSoANullPropertyClearsIt()
	{
		await _db.SetExpandedServerData("PartialUpdateExample", new PartialUpdateExample("Dog", "Bark"));
		await _db.SetExpandedServerData("PartialUpdateExample", new PartialUpdateExample(null, "Bark"));

		var result = await _db.GetExpandedServerData<PartialUpdateExample>("PartialUpdateExample");

		await Assert.That(result).IsEquivalentTo(new PartialUpdateExample(null, "Bark"));
	}

	[Test]
	public async Task GetServerStateAsyncReturnsSetupNotCompletedAfterMigration()
	{
		var state = await _db.GetServerStateAsync();

		await Assert.That(state.SetupCompleted).IsFalse();
	}

	[Test]
	public async Task SetServerSetupCompletedAsyncRoundTrips()
	{
		await _db.SetServerSetupCompletedAsync(true);

		var state = await _db.GetServerStateAsync();

		await Assert.That(state.SetupCompleted).IsTrue();
	}
}
