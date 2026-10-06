using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Records staff actions in the audit log (<see cref="IAuditStore"/>). Every staff action is recorded
/// once, where it is taken: an in-game command records itself as its executor's, and a portal endpoint
/// that changes state without a command records itself as its account's.
/// </summary>
/// <remarks>
/// A portal endpoint that does its work by running a command (through the engine's command invoker)
/// opens <see cref="BeginPortal"/> around it, so the command's own record names the account and says the
/// action came from the portal, and nothing is recorded twice.
/// <para>A record that cannot be written is logged and dropped: the action it describes has already
/// happened, and failing the request afterwards would only hide that.</para>
/// </remarks>
public interface IAuditLog
{
	/// <summary>Records an action taken by <paramref name="executor"/>, in the game or inside a portal scope.</summary>
	ValueTask RecordAsync(AnySharpObject executor, string action, AuditTarget? target, string? details = null,
		CancellationToken ct = default);

	/// <summary>Records an action taken in the portal by the account <paramref name="accountId"/>.</summary>
	ValueTask RecordPortalAsync(string accountId, string action, AuditTarget? target, string? details = null,
		CancellationToken ct = default);

	/// <summary>Records an action the server took by itself, such as lifting a ban that ran out.</summary>
	ValueTask RecordSystemAsync(string action, AuditTarget? target, string? details = null,
		CancellationToken ct = default);

	/// <summary>
	/// Until disposed, actions recorded on this async flow through <see cref="RecordAsync"/> are the
	/// portal's, taken by <paramref name="accountId"/>. A <paramref name="reason"/> the staff member gave
	/// is added to each one's details.
	/// </summary>
	IDisposable BeginPortal(string accountId, string? reason = null);
}

/// <summary>Builders for <see cref="AuditTarget"/>.</summary>
public static class AuditTargets
{
	/// <summary>A game object: a character when it is a player, an object otherwise.</summary>
	public static AuditTarget Of(AnySharpObject target)
		=> new(target.IsPlayer ? AuditTargetKinds.Character : AuditTargetKinds.Object,
			target.Object().DBRef.ToString(), target.Object().Name);

	public static AuditTarget Of(SharpAccount account)
		=> new(AuditTargetKinds.Account, account.Id ?? account.Username, account.Username);

	public static AuditTarget Of(string kind, string id) => new(kind, id, id);
}
