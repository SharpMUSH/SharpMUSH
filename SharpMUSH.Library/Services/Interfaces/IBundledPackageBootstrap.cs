namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Installs the bundled packages the server ships with, as first boot does, for a caller that has to
/// put them back while the game runs: a PennMUSH import clears them to keep the source's dbrefs.
/// </summary>
public interface IBundledPackageBootstrap
{
	/// <summary>
	/// Installs those of <paramref name="packageIds"/> that are bundled, in dependency order, through the
	/// configured package manager. An attach-mode package whose handler is not configured is skipped.
	/// </summary>
	/// <returns>The packages that are installed afterwards.</returns>
	Task<IReadOnlyList<string>> InstallBundledAsync(IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken);
}
