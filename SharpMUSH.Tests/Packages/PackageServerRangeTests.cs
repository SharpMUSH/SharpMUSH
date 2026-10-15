using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A manifest's engine-version range (<c>requires_server</c>, and <c>verified_server</c> and <c>maximum_server</c>,
/// format 1.6): how it is read, written back, and where a server sits in it. Text in, model out.
/// </summary>
public class PackageServerRangeTests
{
	private readonly PackageManifestService _service = new();

	private static string Manifest(string range) => $"""
		format: 1.6
		package: ranged
		version: 1.0.0
		{range}
		objects:
		  - ref: box
		    type: thing
		    name: Box
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string range) => _service.ParseManifest(Manifest(range));

	private static PackageServerFit FitOn(PackageManifest manifest, string server)
	{
		PackageVersion.TryParse(server, out var version);
		return PackageServerCompatibility.Check(manifest, version).Fit;
	}

	[Test]
	public async Task TheThreeBounds_AreRead()
	{
		var (manifest, warnings) = Parse("""
			requires_server: ">=2.0"
			verified_server: "2.3"
			maximum_server: "3"
			""").Expect<ParsedPackageManifest>();

		await Assert.That(warnings).IsEmpty();
		await Assert.That(manifest.RequiresServer!.ToString()).IsEqualTo(">=2.0.0");
		await Assert.That(manifest.VerifiedServer).IsEqualTo(new PackageVersion(2, 3, 0));
		await Assert.That(manifest.MaximumServer!.ToString()).IsEqualTo("3");
	}

	[Test]
	[Arguments("2", "2.9.9", false)]
	[Arguments("2", "3.0.0", true)]
	[Arguments("2.4", "2.4.7", false)]
	[Arguments("2.4", "2.5.0", true)]
	[Arguments("2.4.1", "2.4.1", false)]
	[Arguments("2.4.1", "2.4.2", true)]
	[Arguments("2", "3.0.0-beta.1", true)]
	public async Task TheMaximum_CoversEveryVersionUnderTheComponentsItLeavesOut(string maximum, string server, bool exceeded)
	{
		await Assert.That(PackageVersionCeiling.TryParse(maximum, out var ceiling)).IsTrue();
		PackageVersion.TryParse(server, out var version);

		await Assert.That(ceiling.IsExceededBy(version)).IsEqualTo(exceeded);
	}

	[Test]
	public async Task TheServer_IsPlacedInTheRange()
	{
		var (manifest, _) = Parse("""
			requires_server: ">=2.1"
			verified_server: "2.4"
			maximum_server: "3"
			""").Expect<ParsedPackageManifest>();

		await Assert.That(FitOn(manifest, "2.0.0")).IsEqualTo(PackageServerFit.BelowMinimum);
		await Assert.That(FitOn(manifest, "2.1.0")).IsEqualTo(PackageServerFit.Compatible);
		// A newer minor than the one tested is still the same major, which is not meant to break anything.
		await Assert.That(FitOn(manifest, "2.9.0")).IsEqualTo(PackageServerFit.Compatible);
		await Assert.That(FitOn(manifest, "3.2.0")).IsEqualTo(PackageServerFit.NewerThanVerified);
		await Assert.That(FitOn(manifest, "4.0.0")).IsEqualTo(PackageServerFit.AboveMaximum);
	}

	[Test]
	public async Task NoRange_FitsEveryServer()
	{
		var (manifest, _) = Parse("").Expect<ParsedPackageManifest>();

		await Assert.That(FitOn(manifest, "0.1.0")).IsEqualTo(PackageServerFit.Compatible);
		await Assert.That(FitOn(manifest, "99.0.0")).IsEqualTo(PackageServerFit.Compatible);
	}

	[Test]
	[Arguments("requires_server: \">=2.0 <3.0\"", "requires_server")]
	[Arguments("requires_server: \"2.0\"", "requires_server")]
	[Arguments("verified_server: \"two\"", "verified_server")]
	[Arguments("maximum_server: \"3.0-beta\"", "maximum_server")]
	[Arguments("maximum_server: \">=3\"", "maximum_server")]
	[Arguments("requires_server: \">=3.0\"\nmaximum_server: \"2\"", "maximum_server")]
	public async Task AMalformedRange_IsRefused(string range, string field)
	{
		var failure = Parse(range).Expect<PackageManifestFailure>();

		await Assert.That(failure.Issues.Any(i => i.Severity == PackageManifestIssueSeverity.Error && i.Path == field)).IsTrue();
	}

	[Test]
	[Arguments("requires_server: \">=2.0\"\nverified_server: \"1.5\"")]
	[Arguments("verified_server: \"3.1\"\nmaximum_server: \"2\"")]
	public async Task AVerifiedVersionOutsideTheRange_Warns(string range)
	{
		var (_, warnings) = Parse(range).Expect<ParsedPackageManifest>();

		await Assert.That(warnings.Any(w => w.Path == "verified_server")).IsTrue();
	}

	[Test]
	public async Task TheRange_SurvivesAWriteAndARead()
	{
		var (original, _) = Parse("""
			requires_server: ">=2.0"
			verified_server: "2.3.1"
			maximum_server: "2.4"
			""").Expect<ParsedPackageManifest>();

		var (again, _) = _service.ParseManifest(PackageManifestWriter.Write(original)).Expect<ParsedPackageManifest>();

		await Assert.That(again.VerifiedServer).IsEqualTo(original.VerifiedServer);
		await Assert.That(again.MaximumServer).IsEqualTo(original.MaximumServer);
	}

	[Test]
	public async Task TheEngineVersion_IsTheAssemblyVersionWithoutItsFourthComponent()
	{
		await Assert.That(EngineVersion.FromAssemblyVersion("2.1.3.0").Current).IsEqualTo(new PackageVersion(2, 1, 3));
		await Assert.That(EngineVersion.FromAssemblyVersion(Implementation.Generated.VersionInfo.SharpMUSHVersion).Current.ToString() + ".0")
			.IsEqualTo(Implementation.Generated.VersionInfo.SharpMUSHVersion);
	}
}
