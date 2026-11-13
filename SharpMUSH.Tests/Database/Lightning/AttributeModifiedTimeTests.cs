using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Each attribute keeps when its value was last written, in Unix milliseconds, stamped from the provider's clock in
/// the write's own transaction.
/// </summary>
public class AttributeModifiedTimeTests
{
	private sealed class ManualClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	private readonly ManualClock _clock = new();
	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-attr-mtime-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null,
			clock: _clock);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		await FixtureDirectoryCleanup.DeleteAsync(_path);
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<long?> ModifiedAsync(params string[] path)
		=> (await _db.GetAttributeAsync(new DBRef(1), path).ToListAsync())[^1].ModifiedTime;

	[Test]
	public async Task ASetStampsTheLeafAndASecondSetMovesIt()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["STAMPED"], MarkupText.Plain("one"), god);
		await Assert.That(await ModifiedAsync("STAMPED")).IsEqualTo(1_700_000_000_000);

		_clock.Now = _clock.Now.AddSeconds(90);
		await _db.SetAttributeAsync(new DBRef(1), ["STAMPED"], MarkupText.Plain("two"), god);
		await Assert.That(await ModifiedAsync("STAMPED")).IsEqualTo(1_700_000_090_000);
	}

	[Test]
	public async Task TwoWritesInOneMillisecondStillMoveTheTime()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["FAST"], MarkupText.Plain("one"), god);
		await _db.SetAttributeAsync(new DBRef(1), ["FAST"], MarkupText.Plain("two"), god);

		await Assert.That(await ModifiedAsync("FAST")).IsEqualTo(1_700_000_000_001);
	}

	[Test]
	public async Task AClockSetBackDoesNotMoveTheTimeBackwards()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["BACK"], MarkupText.Plain("one"), god);

		_clock.Now = _clock.Now.AddMinutes(-10);
		await _db.SetAttributeAsync(new DBRef(1), ["BACK"], MarkupText.Plain("two"), god);

		await Assert.That(await ModifiedAsync("BACK")).IsEqualTo(1_700_000_000_001);
	}

	[Test]
	public async Task AChildWriteLeavesItsBranchTimeAlone()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["TREE"], MarkupText.Plain("root"), god);

		_clock.Now = _clock.Now.AddMinutes(5);
		await _db.SetAttributeAsync(new DBRef(1), ["TREE", "LEAF"], MarkupText.Plain("leaf"), god);

		await Assert.That(await ModifiedAsync("TREE")).IsEqualTo(1_700_000_000_000);
		await Assert.That(await ModifiedAsync("TREE", "LEAF")).IsEqualTo(1_700_000_300_000);
	}

	[Test]
	public async Task ABranchCreatedOnTheWayHasNoTimeOfItsOwn()
	{
		await _db.SetAttributeAsync(new DBRef(1), ["MADE", "BELOW"], MarkupText.Plain("x"), await God());

		await Assert.That(await ModifiedAsync("MADE")).IsNull();
	}

	[Test]
	public async Task ClearingABranchThatKeepsItsChildrenLeavesItWithNoTime()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["KEPT"], MarkupText.Plain("value"), god);
		await _db.SetAttributeAsync(new DBRef(1), ["KEPT", "CHILD"], MarkupText.Plain("child"), god);

		_clock.Now = _clock.Now.AddHours(1);
		await _db.ClearAttributeAsync(new DBRef(1), ["KEPT"]);

		await Assert.That(await ModifiedAsync("KEPT")).IsNull();
	}

	[Test]
	public async Task AnOwnerChangeKeepsTheTime()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["OWNED"], MarkupText.Plain("x"), god);

		_clock.Now = _clock.Now.AddDays(1);
		await _db.SetAttributeOwnerAsync(new DBRef(1), ["OWNED"], god);

		await Assert.That(await ModifiedAsync("OWNED")).IsEqualTo(1_700_000_000_000);
	}
}
