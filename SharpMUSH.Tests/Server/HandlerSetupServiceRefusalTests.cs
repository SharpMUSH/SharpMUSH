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
			Substitute.For<IPackageManifestService>(), new ConfigurationReloadService(),
			NullLogger<HandlerSetupService>.Instance);

		var result = await service.SetAsync(HandlerKinds.Http, new SetHandlerRequest(HandlerModes.Create, null),
			CancellationToken.None);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("my-router depends on http-handler");
		await Assert.That(mediator.ReceivedCalls()).IsEmpty()
			.Because("nothing is looked up or created once the move is refused");
	}
}
