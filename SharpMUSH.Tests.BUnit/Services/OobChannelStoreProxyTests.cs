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
}
