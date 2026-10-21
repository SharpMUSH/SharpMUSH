using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Hands the coordinator's root to the queue entries its operation owns. Entering names the root only;
/// a hold still joins it only while it is live on the current lease, so an entry that runs after the
/// operation was released takes an attempt of its own.
/// </summary>
internal sealed class SemanticWikiPackageOperationScope : IPackageOperationScope
{
	public object? Capture() => SemanticFixturePackageOperation.Ambient;

	public IDisposable Enter(object? captured)
		=> captured is SemanticFixturePackageOperation root ? SemanticFixturePackageOperation.EnterAmbient(root) : Nothing.Instance;

	private sealed class Nothing : IDisposable
	{
		internal static readonly Nothing Instance = new();
		public void Dispose() { }
	}
}
