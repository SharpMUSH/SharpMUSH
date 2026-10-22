namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Carries a provider's per-operation context into the queue entries a package operation owns. Queue
/// entries do not run in the flow that started the operation, so <see cref="PackageOperationRunner"/>
/// captures this once, in that flow, and enters it in the operation's body entry and in each lifecycle
/// hook entry it admits. Entering grants nothing by itself: whatever the context names must still be
/// checked to be live where it is used, since an entry can run after the operation has ended.
/// </summary>
public interface IPackageOperationScope
{
	/// <summary>The context of the flow starting an operation; null when there is none.</summary>
	object? Capture();

	/// <summary>Makes <paramref name="captured"/> current for one owned queue entry until disposed.</summary>
	IDisposable Enter(object? captured);
}
