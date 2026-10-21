using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

public partial class PackageOperationRunnerTests
{
	[Test]
	public async Task CommittedInstallHookRunsBeforeAnOuterBodyFailureIsReported()
	{
		var id = $"runner-fault-hooks-{Guid.NewGuid():N}";
		var manifest = new PackageManifestService().ParseManifest($"""
			package: {id}
			version: "1.0"
			objects:
			  - ref: marker
			    type: thing
			    name: Runner Fault Hook Marker
			    attributes:
			      AINSTALL: |-
			        &INSTALL_MARKER me=installed
			""") switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};
		var request = new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", "commit-1", "main"),
			new Dictionary<string, string>(), []);
		var error = new IOException("A later batch step failed after this install committed");
		string? objid = null;
		try
		{
			Exception? observed = null;
			try
			{
				await Runner().RunAsync<bool>("apply", async token =>
				{
					var applied = (await Installer.ApplyAsync(manifest, request, token)).Expect<PackageApplyResult>();
					objid = applied.CreatedObjects["marker"];
					throw error;
				}).WaitAsync(TimeSpan.FromSeconds(60));
			}
			catch (Exception failure) { observed = failure; }
			await Assert.That(ReferenceEquals(observed, error)).IsTrue();
			if (objid is not string installedObject) throw new InvalidOperationException("The install did not commit");
			await Assert.That(await MarkerAsync(installedObject)).IsEqualTo("installed")
				.Because("an outer body failure must not discard the committed install's deferred AINSTALL");
		}
		finally
		{
			if (objid is not null) (await Installer.UninstallAsync(id)).Expect<Success>();
		}
	}
}
