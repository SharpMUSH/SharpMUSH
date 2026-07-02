using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class SessionSinkTests
{
	private sealed class DummyTransport : IDuplexTransport
	{
		public string Kind => "fake";
		public string RemoteIp => "ip";
		public string Hostname => "host";
		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => Task.CompletedTask;
		public Task<string?> ReceiveTextAsync(CancellationToken ct) => Task.FromResult<string?>(null);
		public Task CloseAsync() => Task.CompletedTask;
	}

	[Test]
	public async Task Attach_then_Detach_updates_Current()
	{
		var sink = new SessionSink();
		await Assert.That(sink.Current).IsNull();
		var t = new DummyTransport();
		sink.Attach(t);
		await Assert.That(sink.Current).IsSameReferenceAs(t);
		sink.Detach();
		await Assert.That(sink.Current).IsNull();
	}

	[Test]
	public async Task Registry_GetOrCreate_is_stable_and_Remove_clears()
	{
		var reg = new SessionSinkRegistry();
		var a = reg.GetOrCreate(5);
		var b = reg.GetOrCreate(5);
		await Assert.That(a).IsSameReferenceAs(b);
		reg.Remove(5);
		await Assert.That(reg.Get(5)).IsNull();
	}
}
