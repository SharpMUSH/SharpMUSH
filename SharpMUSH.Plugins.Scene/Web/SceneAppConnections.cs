using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;

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

	private readonly ConcurrentDictionary<string, (string Token, DBRef Character, HubCallerContext Context, bool SeesEvery)> _open = new(StringComparer.Ordinal);

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
		=> _open[connectionId] = (token, character, context, false);

	/// <summary>
	/// Notes that a connection joined a scene's every-scene group (the staff view of hidden scenes), so the check closes
	/// it once the character no longer sees every scene.
	/// </summary>
	public void SeesEvery(string connectionId)
	{
		if (_open.TryGetValue(connectionId, out var open)) _open[connectionId] = open with { SeesEvery = true };
	}

	/// <summary>Forgets a connection that closed.</summary>
	public void Closed(string connectionId) => _open.TryRemove(connectionId, out _);

	/// <summary>The app connections being watched.</summary>
	public int Count => _open.Count;

	/// <summary>
	/// Whether <paramref name="token"/> is still a live access token for <paramref name="character"/> carrying
	/// <see cref="DelegatedScopes.ScenesRead"/>: not expired, not revoked, its grant not taken back (a ban takes back
	/// every grant of the account), and, as the OAuth bearer scheme checks on every request, its account still active
	/// and still holding the character, so unlinking the character stops the app too.
	/// </summary>
	public async ValueTask<bool> StillAllowedAsync(string token, DBRef character, CancellationToken ct = default)
		=> services.GetService<IOAuthService>() is { } oauth
			&& await oauth.ValidateAccessTokenAsync(token, ct) is DelegatedAccess access
			&& access.Token.Character.Number == character.Number
			&& access.Token.Scopes.Contains(DelegatedScopes.ScenesRead, StringComparer.Ordinal)
			&& await AccountStillHoldsAsync(access.Token, ct);

	private async ValueTask<bool> AccountStillHoldsAsync(OAuthToken token, CancellationToken ct)
		=> services.GetService<IAccountService>() is { } accounts
			&& await accounts.GetAccountForCharacterAsync(token.Character, ct) is { } account
			&& account.Id is { } id
			&& string.Equals(KeyOf(id), KeyOf(token.AccountId), StringComparison.Ordinal)
			&& account.IsActive
			&& !account.MustChangePassword;

	/// <summary>The join-time rule for the staff view of hidden scenes: the app was allowed <c>game</c>, and the character still sees every scene.</summary>
	private async ValueTask<bool> StillSeesEveryAsync(HubCallerContext context, DBRef character, CancellationToken ct)
		=> SceneAppAccess.MaySeeEvery(context.User)
			&& services.GetService<ISceneReadAccess>() is { } readAccess
			&& await readAccess.SeesEveryAsync(character, ct);

	/// <summary>An account id without its collection prefix, so <c>node_accounts/k</c> and <c>k</c> compare equal.</summary>
	private static string KeyOf(string accountId) => accountId[(accountId.IndexOf('/') + 1)..];

	/// <summary>Closes every watched connection whose token no longer works.</summary>
	public async Task CheckAsync(CancellationToken ct = default)
	{
		foreach (var (id, (token, character, context, seesEvery)) in _open)
		{
			if (await StillAllowedAsync(token, character, ct) && (!seesEvery || await StillSeesEveryAsync(context, character, ct)))
				continue;
			logger.LogInformation("[SceneHub] Closing app connection {ConnectionId}: its access token or staff view no longer works", id);
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
			logger.LogDebug("[SceneHub] Stopped checking app connections");
		}
	}
}
