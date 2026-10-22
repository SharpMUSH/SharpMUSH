using System.IO.Pipelines;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

/// <summary>
/// A telnet connection's pipe, counting what is written to it. Bytes are waiting from the write until the
/// flush that carries them completes, which is when the transport has taken them: a client that reads
/// slowly holds the flush, and its bytes stay waiting.
/// </summary>
internal sealed class CountingDuplexPipe(IDuplexPipe inner, TrafficCounter counter) : IDuplexPipe
{
	public PipeReader Input => inner.Input;

	public PipeWriter Output { get; } = new CountingPipeWriter(inner.Output, counter);

	private sealed class CountingPipeWriter(PipeWriter inner, TrafficCounter counter) : PipeWriter
	{
		private long _unflushed;

		public override void Advance(int bytes)
		{
			inner.Advance(bytes);
			Interlocked.Add(ref _unflushed, bytes);
			counter.Writing(bytes);
		}

		public override Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);

		public override Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);

		public override void CancelPendingFlush() => inner.CancelPendingFlush();

		public override void Complete(Exception? exception = null) => inner.Complete(exception);

		public override ValueTask CompleteAsync(Exception? exception = null) => inner.CompleteAsync(exception);

		public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
		{
			var flushing = Interlocked.Exchange(ref _unflushed, 0);
			try
			{
				var result = await inner.FlushAsync(cancellationToken);
				// A cancelled flush leaves its bytes in the pipe for the next one.
				if (result.IsCanceled)
					Interlocked.Add(ref _unflushed, flushing);
				else
					counter.Written(flushing);
				return result;
			}
			catch
			{
				counter.Dropped(flushing);
				throw;
			}
		}
	}
}
