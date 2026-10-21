using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Applications;

public class HostedUiPackageTests
{
	[Test]
	public async Task PlainUiPackage_ParsesWithoutAnyAssembly()
	{
		var result = new PackageManifestService().ParseManifest($"""
			package: graph-proof
			version: 1.0.0
			kind: ui
			binaries:
			  min_server_version: ">=3.0"
			  files:
			    - file: ui.json
			      sha256: {new string('a', 64)}
			    - file: index.html
			      sha256: {new string('b', 64)}
			""");
		await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
	}
}
