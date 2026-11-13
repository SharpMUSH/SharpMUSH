using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// An object's modification time follows PennMUSH's <c>ModTime</c>: setting, clearing or wiping an attribute and
/// changing a lock stamp it, unless the object is a player or the attribute is <c>nodump</c>; the importer's batch
/// write and a lock copied or imported leave it alone.
/// </summary>
public class ModificationTimeTests
{
	private static readonly DateTimeOffset Start = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

	private string _path = null!;
	private ManualClock _clock = null!;
	private LightningDatabase _db = null!;
	private SharpPlayer _god = null!;

	private sealed class ManualClock(DateTimeOffset now) : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "modification-time-" + Guid.NewGuid().ToString("N"));
		_clock = new ManualClock(Start);
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null,
			clock: _clock);
		await _db.Migrate();
		_god = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		await FixtureDirectoryCleanup.DeleteAsync(_path);
	}

	private async Task<DBRef> RoomAsync() => await _db.CreateRoomAsync("Room", _god, Start.ToUnixTimeMilliseconds());

	private async Task<long> ModifiedAsync(DBRef dbref)
		=> (await _db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>().Object().ModifiedTime;

	private long Later(int minutes)
	{
		_clock.Now = Start.AddMinutes(minutes);
		return _clock.Now.ToUnixTimeMilliseconds();
	}

	[Test]
	public async Task SettingAnAttributeStampsTheObject()
	{
		var room = await RoomAsync();
		var later = Later(5);

		await _db.SetAttributeAsync(room, ["COLOUR"], MString.Plain("red"), _god);

		await Assert.That(await ModifiedAsync(room)).IsEqualTo(later);
	}

	[Test]
	public async Task ChangingAnAttributeStampsTheObjectAgain()
	{
		var room = await RoomAsync();
		Later(5);
		await _db.SetAttributeAsync(room, ["COLOUR"], MString.Plain("red"), _god);
		var later = Later(9);

		await _db.SetAttributeAsync(room, ["COLOUR"], MString.Plain("blue"), _god);

		await Assert.That(await ModifiedAsync(room)).IsEqualTo(later);
	}

	[Test]
	public async Task ClearingAndWipingAnAttributeStampTheObject()
	{
		var room = await RoomAsync();
		await _db.SetAttributeAsync(room, ["COLOUR"], MString.Plain("red"), _god);
		await _db.SetAttributeAsync(room, ["TREE", "LEAF"], MString.Plain("green"), _god);

		var cleared = Later(5);
		await _db.ClearAttributeAsync(room, ["COLOUR"]);
		await Assert.That(await ModifiedAsync(room)).IsEqualTo(cleared);

		var wiped = Later(9);
		await _db.WipeAttributeAsync(room, ["TREE"]);
		await Assert.That(await ModifiedAsync(room)).IsEqualTo(wiped);
	}

	[Test]
	public async Task AMissingAttributeLeavesTheObjectAlone()
	{
		var room = await RoomAsync();
		Later(5);

		await _db.ClearAttributeAsync(room, ["NOTHERE"]);
		await _db.WipeAttributeAsync(room, ["NOTHERE"]);

		await Assert.That(await ModifiedAsync(room)).IsEqualTo(Start.ToUnixTimeMilliseconds());
	}

	[Test]
	public async Task ANoDumpAttributeLeavesTheObjectAlone()
	{
		var room = await RoomAsync();
		var nodump = (await _db.GetAttributeFlagAsync("nodump"))!;
		await _db.SetAttributesAsync(room, [new AttributeWrite(["SEMAPHORE"], MString.Plain("0"), _god, [nodump])]);
		Later(5);

		await _db.SetAttributeAsync(room, ["SEMAPHORE"], MString.Plain("1"), _god);
		await _db.ClearAttributeAsync(room, ["SEMAPHORE"]);

		await Assert.That(await ModifiedAsync(room)).IsEqualTo(Start.ToUnixTimeMilliseconds());
	}

	[Test]
	public async Task APlayerKeepsItsModificationTime()
	{
		var before = await ModifiedAsync(_god.Object.DBRef);
		Later(5);

		await _db.SetAttributeAsync(_god.Object.DBRef, ["COLOUR"], MString.Plain("red"), _god);

		await Assert.That(await ModifiedAsync(_god.Object.DBRef)).IsEqualTo(before);
	}

	[Test]
	public async Task TheImportersBatchWriteLeavesTheObjectAlone()
	{
		var room = await RoomAsync();
		Later(5);

		await _db.SetAttributesAsync(room, [new AttributeWrite(["COLOUR"], MString.Plain("red"), _god, [])]);

		await Assert.That(await ModifiedAsync(room)).IsEqualTo(Start.ToUnixTimeMilliseconds());
	}

	private async Task<SharpObject> NodeAsync(DBRef room) => (await _db.GetObjectNodeAsync(room)).Expect<AnySharpObject>().Object();

	private static ILockService Locks()
	{
		var locks = Substitute.For<ILockService>();
		locks.SystemLocks.Returns(new Dictionary<string, Library.Services.LockService.LockFlags>());
		locks.ResolveWriteNameAsync(Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => new ValueTask<Result<string>>(call.ArgAt<string>(1)));
		locks.AuthorizeWriteAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<SharpLockData>())
			.Returns(new ValueTask<Result<Success>>(new Success()));
		return locks;
	}

	private static IBooleanExpressionParser Parser()
	{
		var parser = Substitute.For<IBooleanExpressionParser>();
		parser.BindAsync(Arg.Any<string>(), Arg.Any<AnySharpObject>(), Arg.Any<CancellationToken>())
			.Returns(call => new ValueTask<Result<string>>(call.ArgAt<string>(0)));
		return parser;
	}

	[Test]
	public async Task LockingUnlockingAndSettingLockFlagsStampTheObject()
	{
		var room = await RoomAsync();
		var locks = Locks();
		var parser = Parser();

		var locked = Later(5);
		await new SetLockCommandHandler(_db, parser, locks, _clock)
			.Handle(new SetLockCommand(await NodeAsync(room), "Basic", "#1", _god), CancellationToken.None);
		await Assert.That(await ModifiedAsync(room)).IsEqualTo(locked);

		var flagged = Later(9);
		await new SetLockFlagsCommandHandler(_db, locks, _clock)
			.Handle(new SetLockFlagsCommand(await NodeAsync(room), "Basic", Library.Services.LockService.LockFlags.Visual, false, _god),
				CancellationToken.None);
		await Assert.That(await ModifiedAsync(room)).IsEqualTo(flagged);

		var unlocked = Later(13);
		await new UnsetLockCommandHandler(_db, parser, locks, _clock)
			.Handle(new UnsetLockCommand(await NodeAsync(room), "Basic", _god), CancellationToken.None);
		await Assert.That(await ModifiedAsync(room)).IsEqualTo(unlocked);
	}

	[Test]
	public async Task AnImportedLockLeavesTheObjectAlone()
	{
		var room = await RoomAsync();
		Later(5);

		await new ImportLockCommandHandler(_db).Handle(
			new ImportLockCommand(await NodeAsync(room), "Basic", new SharpLockData("#1", default, new DBRef(1))), CancellationToken.None);

		await Assert.That(await ModifiedAsync(room)).IsEqualTo(Start.ToUnixTimeMilliseconds());
	}
}
