namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>Optional native snapshot identity, valid only during one synchronous readonly callback.</summary>
/// <remarks>
/// The number identifies a native read snapshot, not a world, provider, authorization grant or final fence.
/// Compare only within a separately corroborated store instance, reopen epoch and request lifetime.
/// Retaining this interface does not extend the callback; copy the number while the callback is active.
/// </remarks>
public interface IReadSnapshotTx
{
	ulong ReadTransactionId { get; }
}
