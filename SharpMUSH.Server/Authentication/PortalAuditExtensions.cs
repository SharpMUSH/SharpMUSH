using System.Security.Claims;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>Records a portal request's staff action as the request's account.</summary>
public static class PortalAuditExtensions
{
	/// <summary>Records an action the request's account took; nothing when the request carries no account.</summary>
	public static ValueTask RecordPortalAsync(this IAuditLog audit, ClaimsPrincipal? user, string action,
		AuditTarget? target, string? details = null, CancellationToken ct = default)
		=> user?.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } accountId
			? audit.RecordPortalAsync(accountId, action, target, details, ct)
			: ValueTask.CompletedTask;

	/// <summary>
	/// <see cref="IAuditLog.BeginPortal"/> for the request's account, around work done by running a
	/// command: the command's own record then names the account and the portal.
	/// </summary>
	public static IDisposable BeginPortal(this IAuditLog audit, ClaimsPrincipal? user, string? reason = null)
		=> user?.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } accountId
			? audit.BeginPortal(accountId, reason)
			: NoScope.Instance;

	private sealed class NoScope : IDisposable
	{
		public static readonly NoScope Instance = new();

		public void Dispose()
		{
		}
	}
}
