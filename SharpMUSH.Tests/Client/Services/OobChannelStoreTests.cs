using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class OobChannelStoreTests
{
	[Test]
	public async Task SetThenGetReturnsLatestAndRaisesEvent()
	{
		var store = new OobChannelStore();
		string? raised = null;
		store.ChannelUpdated += p => raised = p;

		store.Set("room.contents", "{\"who\":[]}");
		store.Set("room.contents", "{\"who\":[\"#5\"]}");

		await Assert.That(store.Get("room.contents")).IsEqualTo("{\"who\":[\"#5\"]}");
		await Assert.That(raised).IsEqualTo("room.contents");
		await Assert.That(store.Packages).Contains("room.contents");
	}

	[Test]
	public async Task EmptyPackageIsIgnored()
	{
		var store = new OobChannelStore();
		store.Set("", "{\"x\":1}");
		await Assert.That(store.Packages.Count).IsEqualTo(0);
	}
}
