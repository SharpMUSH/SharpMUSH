using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Tells an account's open portal tabs that its roles or grants may have changed, so each reads them again
/// (<c>ReceivePermissionsChanged</c>) instead of keeping what it read at sign-in. It hears every account whose
/// cached claims are dropped: a role or override on the account or on one of its characters (WIZARD, ROYALTY,
/// a power, a chargen package's approved role), a character linked or unlinked, a package's permissions.
/// </summary>
/// <remarks>
/// An account's tabs are its SignalR user: the session puts the account id in
/// <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>, which the default user id provider reads.
/// The send is not awaited, because the change waits on every listener; a tab that misses it reads its
/// authority again when its hub reconnects.
/// </remarks>
public sealed class PortalPermissionNotices(IHubContext<GameHub, IGameHubClient> hub, ILogger<PortalPermissionNotices> logger)
	: IAccountChangeListener
{
	public ValueTask AccountChangedAsync(string accountId, CancellationToken cancellationToken)
	{
		Observe(hub.Clients.User(accountId).ReceivePermissionsChanged());
		return ValueTask.CompletedTask;
	}

	/// <summary>Tells every tab, after a change that can reach any account.</summary>
	public void Everyone() => Observe(hub.Clients.All.ReceivePermissionsChanged());

	private void Observe(Task send) => _ = send.ContinueWith(
		failed => logger.LogWarning(failed.Exception, "Could not tell portal tabs that permissions changed"),
		CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
