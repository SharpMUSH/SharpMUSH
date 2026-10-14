using System.Net.WebSockets;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using SharpMUSH.AnalysisProtocol;
using SharpMUSH.LspBridge;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class BridgeTests
{
	[Test]
	[Arguments("ws://example/lsp")]
	[Arguments("wss://user:password@example/lsp")]
	[Arguments("wss://example/lsp?token=secret")]
	[Arguments("wss://example/lsp#fragment")]
	public async Task EndpointValidation_RejectsInsecureOrCredentialBearingUrls(string value)
		=> await Assert.That(() => RemoteStdioBridge.ValidateEndpoint(value)).Throws<ArgumentException>();

	private sealed class UncancellableInput : MemoryStream
	{
		public TaskCompletionSource<int> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Release.Task);
	}

	[Test]
	public async Task RemoteDisconnect_DoesNotWaitForUncancellableConsoleRead()
	{
		await using var app = await LspIsolatedTransportTests.StartApp(options => options.IdleTimeout = TimeSpan.FromMilliseconds(200));
		using var socket = await LspIsolatedTransportTests.Connect(app);
		using var input = new UncancellableInput(); using var output = new MemoryStream();
		var pump = RemoteStdioBridge.RunAsync(socket, input, output);
		try { await pump.WaitAsync(TimeSpan.FromSeconds(1)); }
		finally { input.Release.TrySetResult(0); await pump; }
	}

	[Test]
	public async Task ActualBridgePump_DeliversShutdownResponseBeforeExit()
	{
		await using var app = await LspIsolatedTransportTests.StartApp();
		using var socket = await LspIsolatedTransportTests.Connect(app);
		var input = new Pipe();
		var output = new Pipe();
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var pump = RemoteStdioBridge.RunAsync(socket, input.Reader.AsStream(), output.Writer.AsStream(), cancellation.Token);
		async Task Write(object value) => await LspFrames.WriteStdioAsync(input.Writer.AsStream(), JsonSerializer.SerializeToUtf8Bytes(value), cancellation.Token);
		async Task<JsonElement> Read()
		{
			using var json = JsonDocument.Parse((await LspFrames.ReadStdioAsync(output.Reader.AsStream(), LspFrames.DefaultMaximumBytes, cancellation.Token))!);
			return json.RootElement.Clone();
		}
		await Write(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { capabilities = new { } } });
		await Assert.That((await Read()).GetProperty("id").GetInt32()).IsEqualTo(1);
		await Write(new { jsonrpc = "2.0", method = "initialized", @params = new { } });
		await Write(new { jsonrpc = "2.0", id = 2, method = "shutdown" });
		await Assert.That((await Read()).GetProperty("result").ValueKind).IsEqualTo(JsonValueKind.Null);
		await Write(new { jsonrpc = "2.0", method = "exit" });
		await pump.WaitAsync(TimeSpan.FromSeconds(5));
		await input.Writer.CompleteAsync();
		await output.Reader.CompleteAsync();
	}

	[Test]
	public async Task ActualBridgeCore_TranslatesStdioFramesThroughVirtualWebSocket()
	{
		await using var app = await LspIsolatedTransportTests.StartApp();
		using var socket = await LspIsolatedTransportTests.Connect(app);
		using var input = new MemoryStream();
		using var output = new MemoryStream();
		await LspFrames.WriteStdioAsync(input, Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"capabilities\":{}}}"));
		// Keep input open until the server's reply is received: request EOF is a disconnection, not shutdown.
		input.Position = 0;
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await RemoteStdioBridge.SendAsync(socket, input, cancellation.Token);
		await RemoteStdioBridge.ReceiveOneAsync(socket, output, cancellation.Token);
		output.Position = 0;
		using var response = JsonDocument.Parse((await LspFrames.ReadStdioAsync(output, LspFrames.DefaultMaximumBytes))!);
		await Assert.That(response.RootElement.GetProperty("id").GetInt32()).IsEqualTo(1);
		await Assert.That(response.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("hoverProvider").GetBoolean()).IsTrue();
	}
}
