using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

[NotInParallel]
public class RemoteBackupAgentAuthenticationTests
{
	[Test]
	public async Task AgentTokenRequiredAndWorkerIdentityValidated()
	{
		var previous = Environment.GetEnvironmentVariable("SHARPMUSH_BACKUP_AGENT_TOKEN");
		const string token = "synthetic-test-token-32-characters-long";
		Environment.SetEnvironmentVariable("SHARPMUSH_BACKUP_AGENT_TOKEN", token);
		try
		{
			var data = Substitute.For<IExpandedObjectDataService>();
			var coordinator = new RemoteBackupCoordinator(data, Substitute.For<ISecretStore>(), new EphemeralDataProtectionProvider(), Substitute.For<IWorldBackupService>(), TimeProvider.System, new("/app/data/lightning.plugins", false, "/app/data/lightning.dataprotection-keys"));
			var controller = new RemoteBackupAgentController(coordinator)
			{
				ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
			};
			await Assert.That(await controller.Claim()).IsTypeOf<UnauthorizedResult>();
			await Assert.That(await controller.Receipt(new(Guid.NewGuid(), "nonce", 0, new string('a', 64), null))).IsTypeOf<UnauthorizedResult>();
			await Assert.That(await controller.Heartbeat()).IsTypeOf<UnauthorizedResult>();
			await data.DidNotReceive().GetExpandedServerDataAsync<RemoteBackupData>();
			controller.Request.Headers["X-Backup-Agent-Token"] = token;
			controller.Request.Headers["X-Backup-Agent-Id"] = "invalid-worker";
			await Assert.That(await controller.Claim()).IsTypeOf<BadRequestResult>();
			controller.Request.Headers["X-Backup-Agent-Id"] = Guid.NewGuid().ToString("N");
			await Assert.That(await controller.Claim()).IsTypeOf<NoContentResult>();
			await Assert.That(controller.Response.Headers.CacheControl.ToString()).IsEqualTo("no-store");
		}
		finally { Environment.SetEnvironmentVariable("SHARPMUSH_BACKUP_AGENT_TOKEN", previous); }
	}

	[Test]
	public async Task AdministrationRequiresServerAdminPolicy()
	{
		var policy = typeof(RemoteBackupsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
		await Assert.That(policy.Policy).IsEqualTo(PortalPermission.ServerAdmin);
	}
}
