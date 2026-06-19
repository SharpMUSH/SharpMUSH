using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Runs softcode lifecycle attributes on a package's objects after a successful
/// apply (decision 20.x): the <c>AINSTALL</c> attribute on a first install and
/// the <c>AUPDATE</c> attribute on an upgrade. This lets a package self-configure
/// after deployment (e.g. register @functions, set @hooks).
///
/// The runner lives in the parser layer (<c>SharpMUSH.Implementation</c>) because
/// evaluating softcode requires the parser, which <c>SharpMUSH.Library</c> must
/// not reference. <see cref="PackageInstallService"/> depends only on this
/// abstraction, so every apply call site (HTTP bootstrap, controller, UI) gets
/// the lifecycle hooks centrally.
/// </summary>
public interface IPackageLifecycleRunner
{
	/// <summary>
	/// After a successful apply, runs the appropriate lifecycle attribute on each
	/// object the package created or attached to:
	/// <list type="bullet">
	/// <item><c>AINSTALL</c> when <paramref name="changeset"/>'s
	/// <see cref="PackageChangeset.Kind"/> is <see cref="PackageRevisionKind.Install"/>.</item>
	/// <item><c>AUPDATE</c> when it is <see cref="PackageRevisionKind.Upgrade"/>.</item>
	/// </list>
	/// The attribute is evaluated as God (#1). Objects lacking the attribute are
	/// skipped silently; a failing lifecycle script is swallowed/logged so a bad
	/// script never fails the install.
	/// </summary>
	/// <param name="changeset">The applied changeset (its <see cref="PackageChangeset.Kind"/> selects the attribute).</param>
	/// <param name="createdObjects">Manifest ref → objid for objects created by this apply (from <see cref="PackageApplyResult.CreatedObjects"/>).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task RunLifecycleAsync(
		PackageChangeset changeset,
		IReadOnlyDictionary<string, string> createdObjects,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs a single lifecycle <paramref name="attribute"/> on one object,
	/// evaluated as God (#1). No-op when the object or attribute is absent;
	/// errors are swallowed/logged.
	/// </summary>
	/// <param name="objId">The target object's objid (<c>#N</c> or <c>#N:created</c>).</param>
	/// <param name="attribute">The lifecycle attribute name (e.g. <c>AINSTALL</c>).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task RunLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default);
}
