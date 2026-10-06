using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The server page's figures (#1565): read in-process from the services that hold them, behind
/// <c>server.admin</c>.
/// </summary>
public class AdminServerControllerTests : ServerTestBase
{
	[Test]
	public async Task OnlyServerAdministratorsReadTheStatus()
	{
		var gate = typeof(AdminServerController).GetCustomAttribute<AuthorizeAttribute>();

		await Assert.That(gate?.Policy).IsEqualTo(PortalPermission.ServerAdmin);
	}

	[Test]
	public async Task TheStatusReportsWhatTheServerHolds()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "SrvStat");
		var controller = ActivatorUtilities.CreateInstance<AdminServerController>(WebAppFactoryArg.Services);

		var result = await controller.Status(CancellationToken.None);

		var status = (AdminServerStatus)((OkObjectResult)result).Value!;
		var options = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var storage = WebAppFactoryArg.Services.GetRequiredService<IStorageCapacityService>().Measure();
		await Assert.That(status.Version).IsEqualTo(Implementation.Generated.VersionInfo.SharpMUSHVersion);
		await Assert.That(status.BuildId).IsNotEmpty();
		await Assert.That(status.StartedAt).IsNotNull();
		await Assert.That(status.Connections).IsGreaterThanOrEqualTo(1)
			.Because($"{player.DbRef} is connected on a handle of its own");
		await Assert.That(status.Players).IsGreaterThanOrEqualTo(1);
		await Assert.That(status.Players).IsLessThanOrEqualTo(status.Connections);
		await Assert.That(status.QueueLimit).IsEqualTo(options.Limit.GlobalQueueLimit);
		await Assert.That(status.Ready).IsEqualTo(status.Pending.Count == 0);
		await Assert.That(status.Storage.MapSizeBytes).IsEqualTo(storage.MapSizeBytes);
		await Assert.That(status.Storage.FileBytes).IsGreaterThan(0);
	}
}
