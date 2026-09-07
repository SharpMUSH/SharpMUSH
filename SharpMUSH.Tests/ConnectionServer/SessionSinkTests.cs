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
		public bool IsSecure => false;
		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => Task.CompletedTask;
		public Task<string?> ReceiveTextAsync(CancellationToken ct) => Task.FromResult<string?>(null);
		public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
	}

	[Test]
	public async Task EndAndAttachRaceNeverLeavesAnEndedSessionAttached()
	{
		for (var attempt = 0; attempt < 100; attempt++)
		{
			var sink = new SessionSink();
			await Task.WhenAll(Task.Run(() => sink.End()), Task.Run(() => sink.TryAttach(new DummyTransport())));
			await Assert.That(sink.Ended).IsTrue();
			await Assert.That(sink.Current).IsNull();
			await Assert.That(sink.TryAttach(new DummyTransport())).IsFalse();
		}
	}

	[Test]
	public async Task OldTransportCannotDetachItsReplacement()
	{
		var sink = new SessionSink();
		var old = new DummyTransport();
		var replacement = new DummyTransport();
		sink.Attach(old);
		sink.Attach(replacement);
		await Assert.That(sink.Detach(old)).IsFalse();
		await Assert.That(sink.Current).IsSameReferenceAs(replacement);
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
