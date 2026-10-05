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

	private static readonly AsyncLocal<string?> PortalAccount = new();

	public async ValueTask RecordAsync(AnySharpObject executor, string action, AuditTarget? target,
		string? details = null, CancellationToken ct = default)
	{
		try
		{
			var character = executor.Object().DBRef;
			var portalAccount = PortalAccount.Value;
			var account = portalAccount is not null
				? await accounts.Value.GetByIdAsync(portalAccount, ct)
				: executor.IsPlayer
					? await accounts.Value.GetAccountForCharacterAsync(character, ct)
					: null;
			var actor = new AuditActor(account?.Id ?? portalAccount, account?.Username, character.ToString(),
				executor.Object().Name);
			await AppendAsync(action, portalAccount is null ? AuditSource.Game : AuditSource.Portal, actor, target,
				details, ct);
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

	public IDisposable BeginPortal(string accountId)
	{
		var outer = PortalAccount.Value;
		PortalAccount.Value = accountId;
		return new Scope(outer);
	}

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

	private sealed class Scope(string? outer) : IDisposable
	{
		public void Dispose() => PortalAccount.Value = outer;
	}
}
