using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using SharpMUSH.Server.Mcp;

namespace SharpMUSH.Tests.Integration.Mcp;

public class McpHttpAdmissionTests
{
	private sealed class BodyLimit(bool readOnly) : IHttpMaxRequestBodySizeFeature
	{
		public bool IsReadOnly => readOnly;
		public long? MaxRequestBodySize { get; set; } = 30_000_000;
	}

	[Test]
	public async Task EndpointBodyFeature_AllowsConfiguredEscapedEnvelopeBeforeRead()
	{
		using var admission = new McpHttpAdmission(Options.Create(new McpOptions()));
		foreach (var readOnly in new[] { false, true })
		{
			var context = Context(new MemoryStream());
			var feature = new BodyLimit(readOnly);
			context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
			await admission.InvokeAsync(context, _ => Task.CompletedTask);
			await Assert.That(feature.MaxRequestBodySize).IsEqualTo(readOnly ? 30_000_000L : 32 * 1024 * 1024L);
		}
	}
	private sealed class BlockingBody : Stream
	{
		public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public bool Cancelled { get; private set; }
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			Started.TrySetResult();
			try { await Task.Delay(Timeout.Infinite, cancellationToken); }
			catch (OperationCanceledException) { Cancelled = true; throw; }
			return 0;
		}
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override void Flush() => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private static DefaultHttpContext Context(Stream body, long? length = null)
	{
		var context = new DefaultHttpContext();
		context.Request.Method = "POST";
		context.Request.Body = body;
		context.Request.ContentLength = length;
		return context;
	}

	[Test]
	public async Task Disposal_CancelsBodyReadAndWaitsToReleaseRequestResources()
	{
		var admission = new McpHttpAdmission(Options.Create(new McpOptions()));
		using var cancellation = new CancellationTokenSource();
		using var body = new BlockingBody();
		var context = Context(body);
		context.RequestAborted = cancellation.Token;
		var pending = admission.InvokeAsync(context, _ => Task.CompletedTask);
		await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
		admission.Dispose();
		try
		{
			await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromMilliseconds(300))).Throws<OperationCanceledException>();
			await Assert.That(body.Cancelled).IsTrue();
		}
		finally
		{
			cancellation.Cancel();
			try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
		}
	}

	[Test]
	public async Task Disposal_DoesNotDisposeAdmissionSlotBeforeActiveHandlerTerminates()
	{
		var admission = new McpHttpAdmission(Options.Create(new McpOptions()));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = admission.InvokeAsync(Context(new MemoryStream()), async _ => { entered.SetResult(); await finish.Task; });
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
		admission.Dispose();
		finish.SetResult();
		await pending.WaitAsync(TimeSpan.FromSeconds(3));
		admission.Dispose();
	}

	[Test]
	public async Task DeclaredAndUndeclaredOversize_AreRejectedBeforeJsonHandler()
	{
		var admission = new McpHttpAdmission(Options.Create(new McpOptions { MaxRequestBodyBytes = 8 }));
		foreach (var length in new long?[] { 9, null })
		{
			var context = Context(new MemoryStream(Encoding.UTF8.GetBytes("123456789")), length);
			var ran = false;
			await admission.InvokeAsync(context, _ => { ran = true; return Task.CompletedTask; });
			await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.RequestEntityTooLarge);
			await Assert.That(ran).IsFalse();
		}
	}

	[Test]
	public async Task ReadTimeout_CancelsActualBodyReadAndReleasesSlot()
	{
		var admission = new McpHttpAdmission(Options.Create(new McpOptions { MaxConcurrentRequests = 1, BodyReadTimeout = TimeSpan.FromMilliseconds(30) }));
		using var body = new BlockingBody();
		var context = Context(body);
		await admission.InvokeAsync(context, _ => throw new InvalidOperationException("Handler must not run")).WaitAsync(TimeSpan.FromSeconds(3));
		await Assert.That(body.Cancelled).IsTrue();
		await Assert.That(context.Response.StatusCode).IsEqualTo(408);
		var next = Context(new MemoryStream(Encoding.UTF8.GetBytes("{}")));
		var ran = false;
		await admission.InvokeAsync(next, _ => { ran = true; return Task.CompletedTask; });
		await Assert.That(ran).IsTrue();
	}

	[Test]
	public async Task ActiveReads_AreBoundedAndRequestAbortReleasesSlotAfterReadStops()
	{
		var admission = new McpHttpAdmission(Options.Create(new McpOptions { MaxConcurrentRequests = 1 }));
		using var cancellation = new CancellationTokenSource();
		using var body = new BlockingBody();
		var first = Context(body);
		first.RequestAborted = cancellation.Token;
		var pending = admission.InvokeAsync(first, _ => Task.CompletedTask);
		await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
		var second = Context(new MemoryStream(Encoding.UTF8.GetBytes("{}")));
		await admission.InvokeAsync(second, _ => throw new InvalidOperationException("Handler must not run"));
		await Assert.That(second.Response.StatusCode).IsEqualTo(429);
		cancellation.Cancel();
		try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
		await Assert.That(body.Cancelled).IsTrue();
		var ran = false;
		await admission.InvokeAsync(Context(new MemoryStream()), _ => { ran = true; return Task.CompletedTask; });
		await Assert.That(ran).IsTrue();
	}
}
