using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// With <c>package_manager</c> unset, the package installer and the profile-handler reset both fell back
/// to <c>#3</c>, which the seed makes the Ancestor Room. The seeded Package Manager is <c>#7</c>, the same
/// number the configuration default names, so the fallback has to name it too.
/// </summary>
public class PackageManagerFallbackTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task AnUnsetPackageManager_FallsBackToTheSeededPackageManagerPlayer()
	{
		var number = DatabaseOptions.PackageManagerOrSeeded(null);

		var node = await Mediator.Send(new GetObjectNodeQuery(new DBRef((int)number)));

		var player = node.Expect<SharpPlayer>();
		await Assert.That(player.Object.Name).IsEqualTo("Package Manager");
	}

	[Test]
	public async Task AConfiguredPackageManager_IsUsedAsConfigured()
	{
		await Assert.That(DatabaseOptions.PackageManagerOrSeeded(42)).IsEqualTo(42u);
	}

	/// <summary>
	/// A game that installed profile-handler 1.5.0 with <c>package_manager</c> unset has <c>#3</c> baked
	/// into <c>FN`CHARVIS</c>, and its baseline says so. Bootstrap skips a same-version package and the
	/// reset refuses a plan that differs from its baselines, so only a newer bundled version carries the
	/// corrected reference to those games.
	/// </summary>
	[Test]
	public async Task TheBundledProfileHandler_IsNewerThanTheVersionThatBakedInTheWrongFallback()
	{
		var match = System.Text.RegularExpressions.Regex.Match(
			BundledPackages.ManifestYaml("profile-handler"), @"^version:\s*(?<v>\S+)", System.Text.RegularExpressions.RegexOptions.Multiline);

		await Assert.That(Version.Parse(match.Groups["v"].Value)).IsGreaterThan(new Version(1, 5, 0));
	}

	[Test]
	public async Task TheConfigurationDefault_IsTheSeededPackageManager()
	{
		await Assert.That(SharpMUSHOptions.Default().Database.PackageManager).IsEqualTo(DatabaseOptions.SeededPackageManager);
	}
}
