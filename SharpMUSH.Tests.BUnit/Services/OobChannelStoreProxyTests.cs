using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

public class OobChannelStoreProxyTests
{
	[Test]
	public async Task Subscriber_taken_before_a_swap_still_hears_the_new_inner()
	{
		var first = new OobChannelStore();
		var sut = new OobChannelStoreProxy();
		sut.SetInner(first);

		string? heard = null;
		sut.ChannelUpdated += p => heard = p;

		var second = new OobChannelStore();
		sut.SetInner(second);
		second.Set("room", "{}");

		await Assert.That(heard).IsEqualTo("room");
	}

	[Test]
	public async Task Old_inner_stops_reaching_subscribers_after_a_swap()
	{
		var first = new OobChannelStore();
		var sut = new OobChannelStoreProxy();
		sut.SetInner(first);

		var heard = 0;
		sut.ChannelUpdated += _ => heard++;

		sut.SetInner(new OobChannelStore());
		first.Set("room", "{}");

		await Assert.That(heard).IsEqualTo(0);
	}

	[Test]
	public async Task Get_reads_through_to_the_current_inner()
	{
		var inner = new OobChannelStore();
		inner.Set("room", "{\"a\":1}");
		var sut = new OobChannelStoreProxy();
		sut.SetInner(inner);

		await Assert.That(sut.Get("room")).IsEqualTo("{\"a\":1}");
	}

	/// <summary>
	/// Task 7 regression: a character switch recreates the terminal, and
	/// <see cref="TerminalServiceHost.Attach"/> re-points this proxy at the new (empty) inner via
	/// <see cref="OobChannelStoreProxy.SetInner"/>. Before the fix, that swap was silent — no
	/// <see cref="IOobChannelStore.ChannelUpdated"/> event at all — so a subscriber like
	/// <c>Play.razor</c>'s sidebar (which only re-reads on that event) kept rendering the PREVIOUS
	/// character's room contents until the new connection happened to push fresh OOB data. The fix
	/// reuses <see cref="IOobChannelStore.Clear"/> on the outgoing store during the swap, which is
	/// documented to raise the event per cleared package.
	/// </summary>
	[Test]
	public async Task Swapping_to_a_new_inner_clears_stale_payloads_and_notifies_subscribers()
	{
		var first = new OobChannelStore();
		first.Set("room", "{\"who\":[\"OldChar\"]}");
		var sut = new OobChannelStoreProxy();
		sut.SetInner(first);
		await Assert.That(sut.Get("room")).IsEqualTo("{\"who\":[\"OldChar\"]}");

		var seen = new List<string>();
		sut.ChannelUpdated += p => seen.Add(p);

		var second = new OobChannelStore();
		sut.SetInner(second);

		// The swap itself notified the subscriber that "room" changed (even though nothing on the
		// NEW inner has been set yet) — that is what lets Play.razor re-read and drop the stale UI
		// immediately instead of waiting for the next server push.
		await Assert.That(seen).Contains("room");
		// And the stale payload is actually gone, not just re-announced.
		await Assert.That(sut.Get("room")).IsNull();
		// The OLD store itself was cleared too — its own data doesn't linger in memory either.
		await Assert.That(first.Get("room")).IsNull();
	}
}
