using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

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
}

/// <summary>
/// The fallback rule itself, which needs no world: <see cref="PackageManagerFallbackTests"/> checks that,
/// in the seeded world, the object it names is the Package Manager.
/// </summary>
public class PackageManagerFallbackRuleTests
{
	[Test]
	public async Task AConfiguredPackageManager_IsUsedAsConfigured()
	{
		await Assert.That(DatabaseOptions.PackageManagerOrSeeded(42)).IsEqualTo(42u);
	}

	[Test]
	public async Task TheConfigurationDefault_IsTheSeededPackageManager()
	{
		await Assert.That(SharpMUSHOptions.Default().Database.PackageManager).IsEqualTo(DatabaseOptions.SeededPackageManager);
	}
}
