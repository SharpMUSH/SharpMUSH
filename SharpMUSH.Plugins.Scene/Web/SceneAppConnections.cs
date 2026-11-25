using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.OAuth;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// The apps' connections to <see cref="SceneHub"/>, and whether each one's access token still works. A SignalR
/// connection keeps the caller it opened with, so without this an app whose player revoked it, or whose account was
/// banned, would keep hearing scenes until its token ran out. <see cref="SceneHub.JoinScene"/> asks
/// <see cref="StillAllowedAsync"/> before each join, and every <see cref="CheckInterval"/> the connections whose token
/// no longer works are closed.
/// </summary>
public sealed class SceneAppConnections(IServiceProvider services, TimeProvider time, ILogger<SceneAppConnections> logger)
	: BackgroundService
{
	/// <summary>How often the open app connections are checked.</summary>
	public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

	private readonly ConcurrentDictionary<string, (string Token, DBRef Character, HubCallerContext Context)> _open = new(StringComparer.Ordinal);

	/// <summary>The access token an app connection opened with: the bearer header, or SignalR's <c>access_token</c> query.</summary>
	public static string? TokenOf(HttpContext? http)
	{
		if (http is null) return null;
		var token = http.Request.Headers.Authorization.FirstOrDefault() is { } header
			&& header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
				? header["Bearer ".Length..].Trim()
				: http.Request.Query["access_token"].FirstOrDefault();
		return OAuthService.IsAccessToken(token) ? token : null;
	}

	/// <summary>Records an app's connection, to be closed when its token stops working.</summary>
	public void Opened(string connectionId, string token, DBRef character, HubCallerContext context)
		=> _open[connectionId] = (token, character, context);

	/// <summary>Forgets a connection that closed.</summary>
	public void Closed(string connectionId) => _open.TryRemove(connectionId, out _);

	/// <summary>The app connections being watched.</summary>
	public int Count => _open.Count;

	/// <summary>
	/// Whether <paramref name="token"/> is still a live access token for <paramref name="character"/> carrying
	/// <see cref="DelegatedScopes.ScenesRead"/>: not expired, not revoked, its grant not taken back (a ban takes back
	/// every grant of the account).
	/// </summary>
	public async ValueTask<bool> StillAllowedAsync(string token, DBRef character, CancellationToken ct = default)
		=> services.GetService<IOAuthService>() is { } oauth
			&& await oauth.ValidateAccessTokenAsync(token, ct) is DelegatedAccess access
			&& access.Token.Character.Number == character.Number
			&& access.Token.Scopes.Contains(DelegatedScopes.ScenesRead, StringComparer.Ordinal);

	/// <summary>Closes every watched connection whose token no longer works.</summary>
	public async Task CheckAsync(CancellationToken ct = default)
	{
		foreach (var (id, (token, character, context)) in _open)
		{
			if (await StillAllowedAsync(token, character, ct)) continue;
			logger.LogInformation("[SceneHub] Closing app connection {ConnectionId}: its access token no longer works", id);
			_open.TryRemove(id, out _);
			context.Abort();
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(CheckInterval, time);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					await CheckAsync(stoppingToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogWarning(ex, "[SceneHub] Checking app connections failed; trying again next time");
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}
}
