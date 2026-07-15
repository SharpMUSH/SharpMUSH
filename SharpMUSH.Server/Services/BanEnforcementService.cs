using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The chokepoint every ban flows through. Two entry points:
/// <see cref="EnforceAccountBanAsync"/> (an account is disabled) and
/// <see cref="EnforceHostRuleAsync"/> (a sitelock host rule is added). Each runs the same three
/// fan-outs against whatever it matches: revoke <see cref="IAccountSessionStore"/> sessions, drop
/// live telnet/websocket game handles via <see cref="IMessageBus"/>'s
/// <see cref="DisconnectConnectionMessage"/>, and abort live SignalR connections via
/// <see cref="HubConnectionRegistry"/>.
/// </summary>
/// <remarks>
/// <see cref="EnforceHostRuleAsync"/> matches against connection metadata IP/host with a
/// placeholder exact-string-or-glob comparison — Task 13 (<c>SitelockMatcher</c>) replaces this
/// with proper glob + CIDR matching; see the <c>// TODO Task 13</c> markers below.
/// </remarks>
public sealed class BanEnforcementService(
	IAccountSessionStore sessionStore,
	AccountClaimsService accountClaims,
	IConnectionService connectionService,
	IMessageBus messageBus,
	HubConnectionRegistry registry,
	ILogger<BanEnforcementService> logger)
{
	/// <summary>The sentinel IP/host value connections use when no real origin is known.</summary>
	private const string UnknownOrigin = "unknown";

	/// <summary>
	/// Revokes every session for <paramref name="accountId"/>, invalidates its cached
	/// role/permission claims, disconnects every live telnet/websocket handle bound to the
	/// account, and aborts every live SignalR connection for the account.
	/// </summary>
	/// <remarks>
	/// Every fan-out below is independently guarded (see <see cref="RunGuardedAsync"/> and the
	/// per-handle try/catch in the connection walk): a ban must land as completely as possible, so
	/// one failing step (e.g. a single un-droppable connection) must never prevent the others from
	/// running.
	/// </remarks>
	public async ValueTask EnforceAccountBanAsync(string accountId, CancellationToken ct = default)
	{
		await RunGuardedAsync("revoke sessions", accountId, () => sessionStore.RevokeAllForAccountAsync(accountId, ct));
		await RunGuardedAsync("invalidate claims cache", accountId, () => accountClaims.InvalidateAsync(accountId).AsTask());
		await RunGuardedAsync("abort SignalR connections", accountId, () =>
		{
			registry.AbortConnectionsForAccount(accountId);
			return Task.CompletedTask;
		});

		await foreach (var connection in connectionService.GetAll().WithCancellation(ct))
		{
			if (!connection.Metadata.TryGetValue("AccountId", out var connectionAccountId)
					|| connectionAccountId != accountId)
			{
				continue;
			}

			logger.LogInformation(
				"[BanEnforcement] Disconnecting handle {Handle} for banned account {AccountId}",
				connection.Handle, accountId);

			try
			{
				await messageBus.Publish(new DisconnectConnectionMessage(connection.Handle, "Account banned"), ct);
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"[BanEnforcement] Failed to publish disconnect for handle {Handle} (account {AccountId}); continuing with remaining connections",
					connection.Handle, accountId);
			}
		}
	}

	/// <summary>
	/// For every live game connection or SignalR connection whose IP or hostname matches
	/// <paramref name="hostPattern"/>, revokes sessions from that connection's origin IP,
	/// disconnects the game handle, and aborts the SignalR connection.
	/// </summary>
	/// <remarks>
	/// The connection walk (per-handle publish), the session-revoke fan-out, and the SignalR-abort
	/// fan-out are each independently guarded (see <see cref="RunGuardedAsync"/> and the per-item
	/// try/catch inside each loop): a ban must land as completely as possible, so one failing step
	/// must never prevent the others from running.
	/// </remarks>
	public async ValueTask EnforceHostRuleAsync(string hostPattern, CancellationToken ct = default)
	{
		var matchedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		await foreach (var connection in connectionService.GetAll().WithCancellation(ct))
		{
			// TODO Task 13: use SitelockMatcher (glob + CIDR) instead of this placeholder
			// exact-string/simple-glob comparison.
			if (!MatchesHostPattern(connection.InternetProtocolAddress, hostPattern)
					&& !MatchesHostPattern(connection.HostName, hostPattern))
			{
				continue;
			}

			// Track the concrete IP for the session-revoke/registry-abort fan-outs below, but
			// never the "unknown" sentinel (e.g. the hostname field matched while the IP field
			// itself was never resolved) — that bucket is shared by every connection with no
			// known origin, not just the ones this rule actually bans. This tracking happens
			// before the publish below (and outside its try/catch) so a publish failure on one
			// handle can never drop an IP from the session-revoke/registry-abort fan-outs.
			if (!string.Equals(connection.InternetProtocolAddress, UnknownOrigin, StringComparison.OrdinalIgnoreCase))
			{
				matchedIps.Add(connection.InternetProtocolAddress);
			}

			logger.LogInformation(
				"[BanEnforcement] Disconnecting handle {Handle} (ip {Ip}) for host rule {Pattern}",
				connection.Handle, connection.InternetProtocolAddress, hostPattern);

			try
			{
				await messageBus.Publish(
					new DisconnectConnectionMessage(connection.Handle, $"Host banned: {hostPattern}"), ct);
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"[BanEnforcement] Failed to publish disconnect for handle {Handle} (ip {Ip}, pattern {Pattern}); continuing with remaining connections",
					connection.Handle, connection.InternetProtocolAddress, hostPattern);
			}
		}

		// The pattern itself counts as an extra revoke/abort target when it is already a literal
		// IP (the common case before Task 13 adds CIDR/glob awareness to the session store's
		// lookup). Never target the literal "unknown" sentinel — a session legitimately carries
		// that origin IP when the client's remote address could not be resolved (see
		// AuthController.ClientIp()), and it is not what an admin means when banning a host.
		var literalTarget = !IsGlobPattern(hostPattern)
				&& !string.Equals(hostPattern, UnknownOrigin, StringComparison.OrdinalIgnoreCase)
			? hostPattern
			: null;

		// Revoke sessions from every concrete IP we found live traffic from, plus the literal
		// pattern target. Each IP is independently guarded so one un-revokable IP doesn't block
		// the rest.
		await RunGuardedAsync("revoke sessions for host rule", hostPattern, async () =>
		{
			foreach (var ip in matchedIps)
			{
				try
				{
					await sessionStore.RevokeAllForIpAsync(ip, ct);
				}
				catch (Exception ex)
				{
					logger.LogError(ex,
						"[BanEnforcement] Failed to revoke sessions for ip {Ip} (pattern {Pattern}); continuing with remaining ips",
						ip, hostPattern);
				}
			}

			if (literalTarget is not null)
			{
				await sessionStore.RevokeAllForIpAsync(literalTarget, ct);
			}
		});

		// Abort SignalR connections. HubConnectionRegistry only supports exact-IP lookups, so
		// abort every concrete IP discovered live above, plus the literal pattern target. Never
		// abort the "unknown" bucket directly — that would tear down every IP-less hub connection,
		// not just the ones this rule actually bans. (AbortConnectionsForIp is itself internally
		// guarded per-connection; this outer guard is defense in depth so a revoke failure above
		// can never skip this fan-out.)
		await RunGuardedAsync("abort SignalR connections for host rule", hostPattern, () =>
		{
			foreach (var ip in matchedIps)
			{
				registry.AbortConnectionsForIp(ip);
			}

			if (literalTarget is not null)
			{
				registry.AbortConnectionsForIp(literalTarget);
			}

			return Task.CompletedTask;
		});
	}

	/// <summary>
	/// Runs <paramref name="action"/> and, if it throws, logs the failure and swallows it so the
	/// remaining ban-enforcement fan-outs still run. A ban must land as completely as possible even
	/// when one step fails.
	/// </summary>
	private async ValueTask RunGuardedAsync(string step, string target, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			logger.LogError(ex,
				"[BanEnforcement] Step '{Step}' failed for {Target}; continuing with remaining enforcement steps",
				step, target);
		}
	}

	private static bool IsGlobPattern(string pattern) => pattern.Contains('*') || pattern.Contains('?');

	/// <summary>
	/// Placeholder host-rule matcher: exact string match, or a simple <c>*</c>/<c>?</c> glob.
	/// Never matches the "unknown"/"UNKNOWN" sentinel a connection carries when it has no known
	/// origin, so an overly broad rule (e.g. <c>"*"</c>) can't sweep up IP-less connections.
	/// </summary>
	private static bool MatchesHostPattern(string? candidate, string pattern)
	{
		if (string.IsNullOrEmpty(candidate) || string.Equals(candidate, UnknownOrigin, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!IsGlobPattern(pattern))
		{
			return string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase);
		}

		var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
		return Regex.IsMatch(candidate, regexPattern, RegexOptions.IgnoreCase);
	}
}
