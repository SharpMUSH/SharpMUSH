using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <see cref="IReadMarkerStore"/> against the Lightning provider directly: a character's last-read
/// marker per channel and per page conversation, which the portal's unread counts are computed from so
/// they survive a reload and a change of device.
/// </summary>
public class ReadMarkersTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
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
				// Best-effort, as the other Lightning fixtures: a lingering mdb.lck can outlive the writer.
			}
		}
	}

	private static readonly DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

	private async Task<DBRef> NewPlayer(string name)
	{
		var created = await _db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0);
		return (await _db.GetObjectNodeAsync(created)).Expect<SharpPlayer>().Object.DBRef;
	}

	[Test]
	public async Task AMarker_IsReadBackForItsCharacterOnly()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");

		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("channel:pub", 10, At));
		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("page:#7:2", null, At.AddMinutes(1)));
		await _db.AdvanceReadMarkerAsync(wren, new SharpReadMarker("channel:pub", 99, At.AddHours(1)));

		var markers = await _db.GetReadMarkersAsync(ilsa);

		await Assert.That(markers).IsEquivalentTo(new[]
		{
			new SharpReadMarker("channel:pub", 10, At),
			new SharpReadMarker("page:#7:2", null, At.AddMinutes(1))
		});
	}

	/// <summary>
	/// Two devices mark the same channel read at different points, and the request from the one that is
	/// further behind can land second. A marker only moves forward, so it never un-reads anything.
	/// </summary>
	[Test]
	public async Task AMarker_OnlyMovesForward()
	{
		var ilsa = await NewPlayer("Ilsa");

		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("channel:pub", 20, At.AddMinutes(2)));
		var kept = await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("channel:pub", 10, At.AddMinutes(5)));
		var moved = await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("channel:pub", 30, At.AddMinutes(3)));

		await Assert.That(kept).IsEqualTo(new SharpReadMarker("channel:pub", 20, At.AddMinutes(2)))
			.Because("an earlier line, whatever the time it was sent, does not move the marker back");
		await Assert.That(moved).IsEqualTo(new SharpReadMarker("channel:pub", 30, At.AddMinutes(3)));
		await Assert.That((await _db.GetReadMarkersAsync(ilsa)).Single()).IsEqualTo(moved);
	}

	/// <summary>A page has no id yet (there is no page history), so its marker compares by time.</summary>
	[Test]
	public async Task AMarkerWithoutAnId_MovesForwardByTime()
	{
		var ilsa = await NewPlayer("Ilsa");

		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("page:#7:2", null, At.AddMinutes(2)));
		var kept = await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("page:#7:2", null, At));
		var moved = await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("page:#7:2", null, At.AddMinutes(9)));

		await Assert.That(kept.LastReadAt).IsEqualTo(At.AddMinutes(2));
		await Assert.That(moved.LastReadAt).IsEqualTo(At.AddMinutes(9));
	}

	/// <summary>
	/// Markers are the character's, not the dbref's: a player who takes a recycled number does not
	/// inherit what the previous holder had read.
	/// </summary>
	[Test]
	public async Task AnotherObjectOnTheSameNumber_HasNoneOfItsMarkers()
	{
		var ilsa = await NewPlayer("Ilsa");
		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("channel:pub", 10, At));

		var successor = new DBRef(ilsa.Number, ilsa.CreationMilliseconds + 1);

		await Assert.That(await _db.GetReadMarkersAsync(successor)).IsEmpty();
	}

	[Test]
	public async Task DestroyingTheCharacter_DropsItsMarkers()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("channel:pub", 10, At));
		await _db.AdvanceReadMarkerAsync(wren, new SharpReadMarker("channel:pub", 11, At));

		await _db.DeleteObjectAsync(ilsa);

		var left = _db.Store.Read(tx => tx.Range(Tables.ReadMarker, []).Count());
		await Assert.That(left).IsEqualTo(1).Because("only the destroyed character's marker goes");
	}

	/// <summary>A channel's id is its name, so renaming it re-files every marker on it under the new id.</summary>
	[Test]
	public async Task RenamingAChannel_MovesItsMarkers()
	{
		var ilsa = await NewPlayer("Ilsa");
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		await _db.CreateChannelAsync(MarkupText.Plain("Public"), ["Player"], god);
		var channel = (await _db.GetChannelAsync("Public"))!;
		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker(ReadMarkerScope.Channel(channel.Id!), 10, At));
		await _db.AdvanceReadMarkerAsync(ilsa, new SharpReadMarker("page:#7:2", null, At));

		await _db.UpdateChannelAsync(channel, MarkupText.Plain("Commons"), null, null, null, null, null, null, null, null, null);

		var renamed = (await _db.GetChannelAsync("Commons"))!;
		await Assert.That(await _db.GetReadMarkersAsync(ilsa)).IsEquivalentTo(new[]
		{
			new SharpReadMarker(ReadMarkerScope.Channel(renamed.Id!), 10, At),
			new SharpReadMarker("page:#7:2", null, At)
		});
	}

	[Test]
	public async Task ACharacterNamedWithoutItsCreationTime_IsRefused()
	{
		await Assert.That(async () => await _db.AdvanceReadMarkerAsync(new DBRef(1), new SharpReadMarker("channel:pub", 1, At)))
			.Throws<ArgumentException>();
	}
}
