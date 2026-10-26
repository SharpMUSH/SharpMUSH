using SharpMUSH.Library.DiscriminatedUnions;

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
	/// <remarks>Then installs the bridges that have both of their halves, as <see cref="InstallBridgesAsync"/> does.</remarks>
	Task<IReadOnlyList<string>> InstallBundledAsync(IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken);

	/// <summary>
	/// Installs each bundled bridge (a <c>&lt;pkg&gt;-help</c> or <c>&lt;pkg&gt;-config</c> registration) whose packages
	/// are all installed, unless the administrator removed it. Called after a package arrives, so its topics and
	/// settings arrive with it.
	/// </summary>
	Task InstallBridgesAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Uninstalls the bundled bridges installed over <paramref name="packageId"/>, before it goes itself. They are not
	/// recorded as turned off, so putting the package back brings them back.
	/// </summary>
	Task<Result<Success>> UninstallBridgesOverAsync(string packageId, CancellationToken cancellationToken);

	/// <summary>
	/// The handler option (<c>http_handler</c> or <c>event_handler</c>) that <paramref name="packageId"/>
	/// attaches to, or <see langword="null"/> when it is not bundled or creates its own objects.
	/// </summary>
	string? RequiredHandlerOption(string packageId);
}
