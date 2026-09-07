using System.Collections.Concurrent;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.ConnectionServer.Services;

public interface ISessionResumeAuthorizationService
{
	Task<bool> AuthorizeAsync(long handle, string session, IDuplexTransport transport, CancellationToken ct);
}

public sealed class SessionResumeAuthorizationService(IMessageBus bus, NatsConsumerRegistry consumers)
	: ISessionResumeAuthorizationService
{
	private readonly ConcurrentDictionary<Guid, TaskCompletionSource<SessionResumeResponseMessage>> _pending = new();

	public async Task<bool> AuthorizeAsync(long handle, string session, IDuplexTransport transport, CancellationToken ct)
	{
		await consumers.WaitUntilReadyAsync(ct);
		var id = Guid.NewGuid();
		var completion = new TaskCompletionSource<SessionResumeResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;
		try
		{
			await bus.Publish(new SessionResumeRequestMessage(id, handle, session,
				transport.RemoteIp, transport.Hostname, transport.IsSecure), ct);
			var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
			if (response.Handle != handle || response.SessionId != session) return false;
			if (response.Retryable) throw new IOException("Engine authorization is temporarily unavailable.");
			return response.Accepted;
		}
		finally { _pending.TryRemove(id, out _); }
	}

	public void Complete(SessionResumeResponseMessage response)
	{
		if (_pending.TryGetValue(response.RequestId, out var pending)) pending.TrySetResult(response);
	}
}

public sealed class SessionResumeResponseConsumer(SessionResumeAuthorizationService authorization)
	: IMessageConsumer<SessionResumeResponseMessage>
{
	public Task HandleAsync(SessionResumeResponseMessage message, CancellationToken cancellationToken = default)
	{
		authorization.Complete(message);
		return Task.CompletedTask;
	}
}
