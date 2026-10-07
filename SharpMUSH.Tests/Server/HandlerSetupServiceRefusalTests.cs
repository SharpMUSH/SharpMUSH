using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// A handler change that a package which is not bundled blocks is refused before anything changes — for a new
/// handler, before the object is made, so a refusal leaves no WIZARD thing behind in the master room.
/// </summary>
public class HandlerSetupServiceRefusalTests
{
	[Test]
	public async Task ANewHandlerThatAPackageBlocks_IsNotMade()
	{
		var mediator = Substitute.For<IMediator>();
		var registry = Substitute.For<IPackageRegistryService>();
		var installed = new InstalledPackageRecord("http-handler", "1.0.0", "bundled", null, "bundled", null,
			DateTimeOffset.UnixEpoch, 1);
		registry.GetInstalledPackageAsync(Arg.Any<string>()).Returns(new NotFound());
		registry.GetInstalledPackageAsync("http-handler").Returns(installed);
		registry.GetPackageDependentsAsync(Arg.Any<string>()).Returns([]);
		registry.GetPackageDependentsAsync("http-handler")
			.Returns([new PackageDependencyRecord("my-router", "http-handler", "*")]);

		var service = new HandlerSetupService(mediator, Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), registry,
			Substitute.For<IPackageInstallService>(), Substitute.For<IBundledPackageBootstrap>(),
			Substitute.For<IPackageManifestService>(), Substitute.For<IConfigOptionWriter>(),
			NullLogger<HandlerSetupService>.Instance);

		var result = await service.SetAsync(HandlerKinds.Http, new SetHandlerRequest(HandlerModes.Create, null),
			CancellationToken.None);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("my-router depends on http-handler");
		await Assert.That(mediator.ReceivedCalls()).IsEmpty()
			.Because("nothing is looked up or created once the move is refused");
	}

	/// <summary>
	/// A package that will not come off the current handler stops the move with the handler unchanged, so the
	/// packages already taken off it go back on rather than leaving the game without them.
	/// </summary>
	[Test]
	public async Task APackageThatWillNotComeOff_PutsBackTheOnesAlreadyRemoved()
	{
		var registry = Substitute.For<IPackageRegistryService>();
		registry.GetInstalledPackageAsync(Arg.Any<string>()).Returns(new NotFound());
		foreach (var id in new[] { "http-handler", "profile-handler" })
		{
			registry.GetInstalledPackageAsync(id).Returns(new InstalledPackageRecord(id, "1.0.0", "bundled", null,
				"bundled", null, DateTimeOffset.UnixEpoch, 1));
		}

		registry.GetPackageDependentsAsync(Arg.Any<string>()).Returns([]);
		var installer = Substitute.For<IPackageInstallService>();
		installer.UninstallAsync("profile-handler", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new Success());
		installer.UninstallAsync("http-handler", Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(new Error<string>("locked"));
		var bundled = Substitute.For<IBundledPackageBootstrap>();
		bundled.InstallBundledAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
			.Returns(call => (IReadOnlyList<string>)call.Arg<IReadOnlyCollection<string>>().ToList());
		var options = TestSharpMushOptions.Create();
		options = options with { Database = options.Database with { HttpHandler = 8 } };

		var service = new HandlerSetupService(Substitute.For<IMediator>(), new TestSharpMushOptions.FixedWrapper(options),
			registry, installer, bundled, Substitute.For<IPackageManifestService>(), Substitute.For<IConfigOptionWriter>(),
			NullLogger<HandlerSetupService>.Instance);

		var result = await service.SetAsync(HandlerKinds.Http, new SetHandlerRequest(HandlerModes.None, null),
			CancellationToken.None);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("http-handler could not be removed");
		await bundled.Received(1).InstallBundledAsync(
			Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "profile-handler" })),
			Arg.Any<CancellationToken>());
	}
}
