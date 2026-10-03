using System.Runtime.CompilerServices;
using System.Text;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.SocketServer.ProtocolHandlers;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Tests.ConnectionServer.TestSchedulers;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// A resume sends replay frames as it reads them (#1461): it never needs the whole history in memory,
/// keeps sequence order, and does not complete when the history stops being complete part-way.
/// </summary>
public class ReplayStreamingPumpTests
{
	[Test]
	public async Task Resume_sends_each_frame_before_the_next_is_read()
	{
		var (pump, token, transport, frames) = Resume(interruptAfter: null);

		await pump.RunAsync(transport, candidateHandle: 99, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

		var seqs = transport.Sent.Where(frame => SeqEnvelope.TryReadSeq(frame, out _)).Select(SeqEnvelope.ReadSeq).ToArray();
		await Assert.That(seqs).IsEquivalentTo(new long[] { 2, 3, 4 });
		await Assert.That(frames.Disposed).IsTrue();
		await Assert.That(transport.Sent.Any(frame => Encoding.UTF8.GetString(frame).Contains("\"token\""))).IsTrue();
		await Assert.That(token).IsNotEmpty();
	}

	[Test]
	public async Task History_that_vanishes_mid_replay_fails_the_resume_instead_of_completing_it()
	{
		var (pump, _, transport, frames) = Resume(interruptAfter: 1);

		await pump.RunAsync(transport, candidateHandle: 99, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

		// One frame went out, then the gap: no new resume token, and the socket is closed so the client
		// reconnects and is told its history is gone rather than silently missing frames.
		await Assert.That(transport.Sent.Count(frame => SeqEnvelope.TryReadSeq(frame, out _))).IsEqualTo(1);
		await Assert.That(transport.Sent.Any(frame => Encoding.UTF8.GetString(frame).Contains("\"token\""))).IsFalse();
		await Assert.That(transport.Closed).IsTrue();
		await Assert.That(frames.Disposed).IsTrue();
	}

	private static (ConnectionPump Pump, string Token, Transport Transport, GatedFrames Frames) Resume(int? interruptAfter)
	{
		var bus = Substitute.For<IMessageBus>();
		var conn = Substitute.For<IConnectionServerService>();
		var desc = Substitute.For<IDescriptorGeneratorService>();
		var resume = new ResumeTokenService();
		var registry = new SessionSinkRegistry();
		conn.Get(9).Returns(new ConnectionServerService.ConnectionData(
			9, null, ConnectionServerService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => { }, null,
			new SharpMUSH.SocketServer.Models.ProtocolCapabilities(), null, "websocket"));
		var session = TestIsolationHelpers.GenerateUniqueName("session");
		var sink = registry.GetOrCreate(9);
		sink.Detach();
		sink.SessionId = session;
		var token = resume.MintAsync(9, session).AsTask().GetAwaiter().GetResult();
		var transport = new Transport($"{{\"type\":\"resume\",\"token\":\"{token}\",\"lastSeq\":1}}");
		var frames = new GatedFrames(transport, [2, 3, 4], interruptAfter);
		var store = Substitute.For<ITerminalReplayStore>();
		store.OpenAsync(session, 1, Arg.Any<CancellationToken>()).Returns(new ReplayOpening(frames));
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, conn, bus, desc, store, resume, registry,
			new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromSeconds(120));
		return (pump, token, transport, frames);
	}

	/// <summary>
	/// Yields a frame only once the previous one has reached the socket, the way a paged read hands a page
	/// on before fetching the next. A pump that collected the history first would never see frame two.
	/// </summary>
	private sealed class GatedFrames(Transport transport, long[] seqs, int? interruptAfter) : ReplayFrames
	{
		public bool Disposed { get; private set; }

		public override async IAsyncEnumerable<byte[]> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
		{
			for (var i = 0; i < seqs.Length; i++)
			{
				if (i == interruptAfter) throw new ReplayInterruptedException();
				if (i > 0)
				{
					var previous = seqs[i - 1];
					await transport.WaitForSeqAsync(previous).WaitAsync(TimeSpan.FromSeconds(5), ct);
				}
				yield return SeqEnvelope.Wrap(seqs[i], Encoding.UTF8.GetBytes($"line {seqs[i]}"));
			}
		}

		public override ValueTask DisposeAsync()
		{
			Disposed = true;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class Transport(string first) : IDuplexTransport
	{
		private readonly Lock _gate = new();
		private readonly Dictionary<long, TaskCompletionSource> _seen = [];
		private bool _first = true;
		public List<byte[]> Sent { get; } = [];
		public bool Closed { get; private set; }
		public string Kind => "websocket";
		public string RemoteIp => "local";
		public string Hostname => "local";
		public bool IsSecure => true;

		public Task WaitForSeqAsync(long seq) => Seen(seq).Task;

		private TaskCompletionSource Seen(long seq)
		{
			lock (_gate)
			{
				if (!_seen.TryGetValue(seq, out var seen))
					_seen[seq] = seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				return seen;
			}
		}

		public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
		{
			var frame = data.ToArray();
			lock (_gate) Sent.Add(frame);
			if (SeqEnvelope.TryReadSeq(frame, out var seq)) Seen(seq).TrySetResult();
			return Task.CompletedTask;
		}

		public Task<string?> ReceiveTextAsync(CancellationToken ct)
		{
			if (!_first) return Task.FromResult<string?>(null);
			_first = false;
			return Task.FromResult<string?>(first);
		}

		public Task CloseAsync(CancellationToken ct = default)
		{
			Closed = true;
			return Task.CompletedTask;
		}
	}
}
