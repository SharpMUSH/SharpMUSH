using SharpMUSH.Server.Services;
using PackageVersion = SharpMUSH.Library.Models.Packages.PackageVersion;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// The version gate that decides whether first-boot bootstrap upgrades an already-installed
/// bundled package. Bootstrap used to skip every installed package unconditionally, so a game
/// kept whatever version of a bundled package it first installed forever — additions like
/// profile-handler's GET`ONLINE route (v1.1.0) never reached games created before it, and the
/// portal's "online now" widget stayed empty with no upgrade path.
/// </summary>
public class BundledPackageUpgradeTests
{
	private static bool IsNewer(string bundled, string? installed) =>
		DefaultPackagesBootstrapService.IsNewer(Parse(bundled), installed);

	private static PackageVersion Parse(string text) =>
		PackageVersion.TryParse(text, out var version) ? version : throw new ArgumentException(text);

	[Test]
	[Arguments("1.1.0", "1.0.0")]
	[Arguments("1.0.1", "1.0.0")]
	[Arguments("2.0.0", "1.9.9")]
	[Arguments("1.0.0", "1.0.0-beta")]
	public async Task NewerBundledVersion_Upgrades(string bundled, string installed)
		=> await Assert.That(IsNewer(bundled, installed)).IsTrue();

	[Test]
	[Arguments("1.0.0", "1.0.0")]
	[Arguments("1.0.0", "1.1.0")]
	[Arguments("1.0.0", "2.0.0")]
	public async Task SameOrOlderBundledVersion_IsLeftAlone(string bundled, string installed)
		=> await Assert.That(IsNewer(bundled, installed)).IsFalse();

	/// <summary>
	/// An installed version the parser cannot read is left to the package manager rather than
	/// overwritten on a guess.
	/// </summary>
	[Test]
	[Arguments("")]
	[Arguments("not-a-version")]
	public async Task UnparseableInstalledVersion_IsLeftAlone(string installed)
		=> await Assert.That(IsNewer("9.9.9", installed)).IsFalse();

	[Test]
	public async Task MissingBundledVersion_IsLeftAlone()
		=> await Assert.That(DefaultPackagesBootstrapService.IsNewer(null, "1.0.0")).IsFalse();
}
