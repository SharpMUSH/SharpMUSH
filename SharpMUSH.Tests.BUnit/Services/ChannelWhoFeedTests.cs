using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="ChannelWhoFeed"/>: the watched channel's member list, read from the server and kept current by
/// <c>comm.who</c>, read again on every <c>comm.channels</c>, and dropped when the store is cleared.
/// </summary>
public class ChannelWhoFeedTests
{
	private readonly OobChannelStore _store = new();
	private readonly FakeCommHistory _server = new();

	private static string Who(string channel, string name, string objid, bool online) =>
		$$"""{"v":2,"channel":"{{channel}}","member":{"name":"{{name}}","objid":"{{objid}}"},"online":{{(online ? "true" : "false")}}}""";

	private const string Channels =
		"""{"v":2,"viewer":{"name":"Ilsa","objid":"#5:1"},"channels":[{"name":"Public","joined":true}]}""";

	private IReadOnlyList<string>? Names(ChannelWhoFeed feed, string channel) =>
		feed.Members(channel)?.Select(member => member.Name).ToList();

	[Test]
	public async Task Watching_ReadsTheList_SortedByName()
	{
		_server.Who["Public"] = [new("Wren", "#12:1"), new("Dace", "#13:1")];
		var feed = new ChannelWhoFeed(_store, _server);

		await Assert.That(feed.Members("Public")).IsNull();
		await feed.WatchAsync("Public");

		await Assert.That(Names(feed, "Public")).IsEquivalentTo(new[] { "Dace", "Wren" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(feed.Members("public")).IsNotNull().Because("a channel name matches without regard to case");
	}

	[Test]
	public async Task APush_AddsOrRemovesOneMember_OfTheWatchedChannelOnly()
	{
		_server.Who["Public"] = [new("Wren", "#12:1")];
		var feed = new ChannelWhoFeed(_store, _server);
		await feed.WatchAsync("Public");
		var changes = 0;
		feed.Changed += () => changes++;

		_store.Set(CommPayloadParser.WhoPackage, Who("Public", "Dace", "#13:1", true));
		_store.Set(CommPayloadParser.WhoPackage, Who("Public", "Dace", "#13:1", true));
		_store.Set(CommPayloadParser.WhoPackage, Who("Public", "Wren", "#12:1", false));
		_store.Set(CommPayloadParser.WhoPackage, Who("OOC", "Tomas", "#20:1", true));

		await Assert.That(Names(feed, "Public")).IsEquivalentTo(new[] { "Dace" });
		await Assert.That(changes).IsEqualTo(3).Because("a push for a channel nobody is watching changes nothing");
	}

	[Test]
	public async Task APush_DuringTheRead_IsAppliedToItsAnswer()
	{
		_server.Who["Public"] = [new("Wren", "#12:1")];
		_server.WhoGate = new TaskCompletionSource();
		var feed = new ChannelWhoFeed(_store, _server);

		var reading = feed.WatchAsync("Public");
		// Dace connected after the server built its answer, Wren left: the answer still names Wren and not Dace.
		_store.Set(CommPayloadParser.WhoPackage, Who("Public", "Dace", "#13:1", true));
		_store.Set(CommPayloadParser.WhoPackage, Who("Public", "Wren", "#12:1", false));
		_server.WhoGate.SetResult();
		await reading;

		await Assert.That(Names(feed, "Public")).IsEquivalentTo(new[] { "Dace" });
	}

	[Test]
	public async Task AChannelList_ReadsTheWatchedListAgain()
	{
		_server.Who["Public"] = [new("Wren", "#12:1")];
		var feed = new ChannelWhoFeed(_store, _server);
		await feed.WatchAsync("Public");

		// What changed while the connection was down is not replayed; the list sent on reconnect says to look again.
		_server.Who["Public"] = [new("Wren", "#12:1"), new("Dace", "#13:1")];
		_store.Set(CommPayloadParser.ChannelsPackage, Channels);

		await Assert.That(_server.WhoRead).IsEquivalentTo(new[] { "Public", "Public" });
		await Assert.That(Names(feed, "Public")).IsEquivalentTo(new[] { "Dace", "Wren" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ClearingTheStore_DropsTheList_AndUnwatchingStopsIt()
	{
		_server.Who["Public"] = [new("Wren", "#12:1")];
		var feed = new ChannelWhoFeed(_store, _server);
		await feed.WatchAsync("Public");
		_store.Set(CommPayloadParser.WhoPackage, Who("Public", "Dace", "#13:1", true));

		_store.Clear();
		await Assert.That(feed.Members("Public")).IsNull();

		await feed.WatchAsync("Public");
		feed.Unwatch("Public");
		await Assert.That(feed.Members("Public")).IsNull();
		_store.Set(CommPayloadParser.ChannelsPackage, Channels);
		await Assert.That(_server.WhoRead.Count).IsEqualTo(2).Because("nothing is watched, so nothing is read again");
	}

	[Test]
	public async Task AMalformedPush_IsIgnored()
	{
		_server.Who["Public"] = [new("Wren", "#12:1")];
		var feed = new ChannelWhoFeed(_store, _server);
		await feed.WatchAsync("Public");

		_store.Set(CommPayloadParser.WhoPackage, """{"v":2,"channel":"Public","member":{"name":"Dace"},"online":true}""");
		_store.Set(CommPayloadParser.WhoPackage, """{"v":2,"channel":"Public","member":{"name":"Dace","objid":"#13:1"},"online":"yes"}""");
		_store.Set(CommPayloadParser.WhoPackage, "not json");

		await Assert.That(Names(feed, "Public")).IsEquivalentTo(new[] { "Wren" });
	}
}
