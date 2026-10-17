using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

public class PluginDependencyResponseTests
{
	[Test]
	public async Task DependenciesReturnStructuredConflict()
	{
		var plugins = Substitute.For<IPluginAdministration>();
		var blockers = new PluginStateBlockers(["content"], ["dependent"]);
		plugins.GetBlockersAsync("owner", false, Arg.Any<CancellationToken>()).Returns(blockers);
		plugins.SetEnabledAsync("owner", false, Arg.Any<CancellationToken>()).Returns(new Error<string>("Has dependents."));
		var controller = new PackagePluginsController(plugins, null!, new(true), Substitute.For<IAuditLog>());
		var result = await controller.Disable("owner", CancellationToken.None);
		await Assert.That(result.Result is ConflictObjectResult { Value: PluginStateBlockers }).IsTrue();
	}

	[Test]
	public async Task OrdinaryErrorsDoNotReturnDependencyConflict()
	{
		var plugins = Substitute.For<IPluginAdministration>();
		plugins.GetBlockersAsync("missing", false, Arg.Any<CancellationToken>()).Returns(new PluginStateBlockers([], []));
		plugins.SetEnabledAsync("missing", false, Arg.Any<CancellationToken>()).Returns(new Error<string>("Unknown plugin."));
		var controller = new PackagePluginsController(plugins, null!, new(true), Substitute.For<IAuditLog>());
		await Assert.That((await controller.Disable("missing", CancellationToken.None)).Result is BadRequestObjectResult).IsTrue();
	}

	[Test]
	[Arguments(nameof(PackagePluginsController.Enable))]
	[Arguments(nameof(PackagePluginsController.Disable))]
	public async Task StateMutationAlsoRequiresServerOperate(string method)
	{
		var authorization = typeof(PackagePluginsController).GetMethod(method)!
			.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
			.Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
		await Assert.That(authorization.Any(a => a.Policy == SharpMUSH.Library.Authorization.PortalPermission.ServerOperate)).IsTrue();
	}
}
