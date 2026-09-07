using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.ConnectionServer;

public class ResumeTokenRefreshTests
{
	[Test]
	public async Task DueTokenIsRefreshedWithoutInvalidatingThePreviousToken()
	{
		var (service, state, tokens, sink, transport) = await CreateAsync();
		sink.TokenIssuedAt = DateTimeOffset.MinValue;
		await service.RefreshAsync(default);
		await tokens.Received(1).MintAsync(1, "session", Arg.Any<CancellationToken>());
		await transport.Received(1).SendAsync(Arg.Is<ReadOnlyMemory<byte>>(bytes =>
			Encoding.UTF8.GetString(bytes.ToArray()).Contains("resumeToken")), Arg.Any<CancellationToken>());
		await tokens.DidNotReceiveWithAnyArgs().InvalidateAsync(default!);
		await Assert.That(sink.TokenIssuedAt > DateTimeOffset.MinValue).IsTrue();
		await state.Received(1).UpdateMetadataAsync(1, "GatewayLastSeen", Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task FreshTokenIsNotRefreshed()
	{
		var (service, state, tokens, sink, _) = await CreateAsync();
		sink.TokenIssuedAt = DateTimeOffset.UtcNow;
		await service.RefreshAsync(default);
		await tokens.DidNotReceiveWithAnyArgs().MintAsync(default, default!);
		await state.Received(1).UpdateMetadataAsync(1, "GatewayLastSeen", Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task DetachedSessionDoesNotReceiveNewCredentials()
	{
		var (service, _, tokens, sink, transport) = await CreateAsync();
		sink.Detach();
		sink.TokenIssuedAt = DateTimeOffset.MinValue;
		await service.RefreshAsync(default);
		await tokens.DidNotReceiveWithAnyArgs().MintAsync(default, default!);
		await transport.DidNotReceiveWithAnyArgs().SendAsync(default, default);
	}

	[Test]
	public async Task TokenFailureDoesNotPreventStateTtlRefreshAndRetriesNextHeartbeat()
	{
		var (service, state, tokens, sink, _) = await CreateAsync();
		sink.TokenIssuedAt = DateTimeOffset.MinValue;
		tokens.MintAsync(1, "session", Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromException<string>(new IOException("store unavailable")));
		await service.RefreshAsync(default);
		await service.RefreshAsync(default);
		await state.Received(2).UpdateMetadataAsync(1, "GatewayLastSeen", Arg.Any<string>(), Arg.Any<CancellationToken>());
		await tokens.Received(2).MintAsync(1, "session", Arg.Any<CancellationToken>());
		await Assert.That(sink.TokenIssuedAt).IsEqualTo(DateTimeOffset.MinValue);
	}

	private static async Task<(ConnectionStateRefreshService Service, IConnectionStateStore State,
		IResumeTokenStore Tokens, SessionSink Sink, IDuplexTransport Transport)> CreateAsync()
	{
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, Substitute.For<IMessageBus>());
		await connections.RegisterAsync(1, "localhost", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { });
		var sinks = new SessionSinkRegistry();
		var sink = sinks.GetOrCreate(1);
		sink.SessionId = "session";
		var transport = Substitute.For<IDuplexTransport>();
		sink.Attach(transport);
		var tokens = Substitute.For<IResumeTokenStore>();
		tokens.MintAsync(1, "session", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult("new-token"));
		var state = Substitute.For<IConnectionStateStore>();
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Replay:RetentionHours"] = "24"
		}).Build();
		return (new ConnectionStateRefreshService(connections, state,
			NullLogger<ConnectionStateRefreshService>.Instance, sinks, tokens, configuration), state, tokens, sink, transport);
	}
}
