using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
/// <remarks>
/// The account service is reached lazily: the services that record (role management among them) are
/// themselves dependencies of things the account service is built from.
/// </remarks>
public sealed class AuditLog(
	IMediator mediator,
	Lazy<IAccountService> accounts,
	ILogger<AuditLog> logger,
	TimeProvider time) : IAuditLog
{
	public AuditLog(IMediator mediator, Lazy<IAccountService> accounts, ILogger<AuditLog> logger)
		: this(mediator, accounts, logger, TimeProvider.System)
	{
	}

	private static readonly AsyncLocal<PortalScope?> Portal = new();

	private sealed record PortalScope(string AccountId, string? Reason);

	public async ValueTask RecordAsync(AnySharpObject executor, string action, AuditTarget? target,
		string? details = null, CancellationToken ct = default)
	{
		try
		{
			var character = executor.Object().DBRef;
			var portal = Portal.Value;
			var portalAccount = portal?.AccountId;
			var account = portalAccount is not null
				? await accounts.Value.GetByIdAsync(portalAccount, ct)
				: executor.IsPlayer
					? await accounts.Value.GetAccountForCharacterAsync(character, ct)
					: null;
			var actor = new AuditActor(account?.Id ?? portalAccount, account?.Username, character.ToString(),
				executor.Object().Name);
			await AppendAsync(action, portalAccount is null ? AuditSource.Game : AuditSource.Portal, actor, target,
				WithReason(details, portal?.Reason), ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Dropped(ex, target);
		}
	}

	public async ValueTask RecordPortalAsync(string accountId, string action, AuditTarget? target,
		string? details = null, CancellationToken ct = default)
	{
		try
		{
			var account = await accounts.Value.GetByIdAsync(accountId, ct);
			await AppendAsync(action, AuditSource.Portal, new AuditActor(accountId, account?.Username, null, null),
				target, details, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Dropped(ex, target);
		}
	}

	public async ValueTask RecordSystemAsync(string action, AuditTarget? target, string? details = null,
		CancellationToken ct = default)
	{
		try
		{
			await AppendAsync(action, AuditSource.System, new AuditActor(null, null, null, "System"), target, details, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Dropped(ex, target);
		}
	}

	public IDisposable BeginPortal(string accountId, string? reason = null)
	{
		var outer = Portal.Value;
		Portal.Value = new PortalScope(accountId, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
		return new Scope(outer);
	}

	/// <summary><paramref name="details"/> with the staff member's reason after it.</summary>
	public static string? WithReason(string? details, string? reason) => (details, reason) switch
	{
		(_, null or "") => details,
		(null or "", _) => $"reason: {reason}",
		_ => $"{details}; reason: {reason}"
	};

	private async ValueTask AppendAsync(string action, AuditSource source, AuditActor actor, AuditTarget? target,
		string? details, CancellationToken ct)
		=> await mediator.Send(new RecordAuditCommand(
			new AuditDraft(time.GetUtcNow(), action, source, actor, target, details)), ct);

	/// <remarks>
	/// Names only the kind of target. The action and the target's id can name an account and what was done
	/// to its password, which the application log is not the place for; the exception says what failed.
	/// </remarks>
	private void Dropped(Exception ex, AuditTarget? target)
		=> logger.LogError(ex, "[Audit] Could not record an entry on a {TargetKind}",
			LogSanitizer.Sanitize(target?.Kind ?? "(none)"));

	private sealed class Scope(PortalScope? outer) : IDisposable
	{
		public void Dispose() => Portal.Value = outer;
	}
}
