using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Server.Consumers;

/// <summary>Rechecks current authorization before a transport attaches to a remembered session.</summary>
public sealed class SessionResumeConsumer(
	IConnectionStateStore store,
	IConnectionService connections,
	IAccountService accounts,
	IMediator mediator,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	IMessageBus bus,
	ILogger<SessionResumeConsumer> logger,
	Microsoft.Extensions.Hosting.IHostApplicationLifetime? lifetime = null) : IMessageConsumer<SessionResumeRequestMessage>
{
	public async Task HandleAsync(SessionResumeRequestMessage message, CancellationToken cancellationToken = default)
	{
		var accepted = false;
		var retryable = false;
		try
		{
			if (lifetime is not null && (!lifetime.ApplicationStarted.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested))
				throw new IOException("Engine startup or shutdown is in progress.");
			accepted = await AuthorizeAsync(message, cancellationToken);
		}
		catch (Exception ex)
		{
			retryable = true;
			logger.LogWarning(ex, "Session resume authorization failed for handle {Handle}", message.Handle);
		}

		await bus.Publish(new SessionResumeResponseMessage(message.RequestId, message.Handle,
			message.SessionId, accepted, retryable), cancellationToken);
	}

	private async Task<bool> AuthorizeAsync(SessionResumeRequestMessage request, CancellationToken ct)
	{
		var persisted = await store.GetConnectionAsync(request.Handle, ct);
		var current = connections.Get(request.Handle);
		if (persisted is null || current is null || string.IsNullOrWhiteSpace(request.SessionId)
			|| persisted.Handle != request.Handle
			|| persisted.Metadata.GetValueOrDefault("SessionId") != request.SessionId
			|| current.Metadata.GetValueOrDefault("SessionId") != request.SessionId
			|| persisted.Metadata.GetValueOrDefault("ResumeRevoked") == "1"
			|| current.Metadata.GetValueOrDefault("ResumeRevoked") == "1"
			|| persisted.ConnectionType != "websocket"
			|| current.Metadata.GetValueOrDefault("ConnectionType") != "websocket"
			|| persisted.State != current.State.ToString()
			|| current.State is not (IConnectionService.ConnectionState.LoggedIn or IConnectionService.ConnectionState.AccountMode)
			|| (persisted.Metadata.GetValueOrDefault("SSL") == "1" && !request.IsSecure))
			return false;

		if (persisted.Metadata.TryGetValue("ResumeExpiresAt", out var expiry)
			&& (!long.TryParse(expiry, out var expiresAt) || expiresAt < 0
				|| (expiresAt > 0 && expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())))
			return false;

		var options = configuration.CurrentValue;
		if (SitelockMatcher.IsBlocked(options.SitelockRules.Rules, request.IpAddress, request.Hostname,
			SitelockMatcher.ConnectFlag)) return false;

		SharpPlayer? player = null;
		if (current.State == IConnectionService.ConnectionState.LoggedIn)
		{
			if (!DBRef.TryParse(persisted.PlayerObjid, out var reference) || reference is not { IsObjid: true }
				|| current.Ref != reference) return false;
			var node = await mediator.Send(new GetObjectNodeQuery(reference.Value), ct);
			if (!node.IsPlayer) return false;
			player = node.AsPlayer;
			if (new DBRef(player.Object.Key, player.Object.CreationTime) != reference.Value) return false;
		}
		else if (persisted.PlayerObjid is not null || current.Ref is not null) return false;

		var accountId = persisted.Metadata.GetValueOrDefault("AccountId");
		if (accountId != current.Metadata.GetValueOrDefault("AccountId")) return false;
		var owner = player is null ? null : await accounts.GetAccountForCharacterAsync(current.Ref!.Value, ct);
		if (accountId is not null)
		{
			var account = await accounts.GetByIdAsync(accountId, ct);
			if (account is not { IsActive: true } || (player is not null && owner?.Id != accountId)) return false;
		}
		else if (player is null) return false;
		if (owner is { IsActive: false }) return false;

		var guest = player is not null && await GuestCharacters.IsGuestAsync(player);
		if (guest && (!options.Net.Guests || SitelockMatcher.IsBlocked(options.SitelockRules.Rules,
			request.IpAddress, request.Hostname, SitelockMatcher.GuestFlag))) return false;
		if (!options.Net.Logins && (guest || player is null || !await new AnySharpObject(player).IsWizard()))
			return false;

		// Authorization never binds a player or emits a second login event. Reconciliation owns that state.
		if (!await store.TryUpdateTransportAsync(request.Handle, request.SessionId, persisted.PlayerObjid,
			persisted.State, request.IpAddress, request.Hostname, request.IsSecure, ct)) return false;
		if (!ReferenceEquals(connections.Get(request.Handle), current)
			|| current.Metadata.GetValueOrDefault("SessionId") != request.SessionId
			|| current.Metadata.GetValueOrDefault("ResumeRevoked") == "1"
			|| current.Metadata.GetValueOrDefault("AccountId") != accountId) return false;
		current.Metadata["InternetProtocolAddress"] = request.IpAddress;
		current.Metadata["HostName"] = request.Hostname;
		current.Metadata["SSL"] = request.IsSecure ? "1" : "0";
		return true;
	}
}
