using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Plugins;
using SharpMUSH.Tests.Shared;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Applications;

public class HostedApplicationControllerTests
{
	[Test]
	public async Task TwoSlugsShareDefinitionButKeepConfigSeparateAndPrivate()
	{
		var directories = PluginPackageFixture.ScratchDirectories();
		try
		{
			var folder = Path.Combine(directories.UiPackages, "graph-proof"); Directory.CreateDirectory(folder);
			var bytes = Encoding.UTF8.GetBytes(UiDefinitionTests.DefinitionJson);
			await File.WriteAllBytesAsync(Path.Combine(folder, "ui.json"), bytes);
			await File.WriteAllTextAsync(Path.Combine(folder, PluginUiBinaryManifest.FileName), new PluginUiBinaryManifest(new Dictionary<string, string> { ["ui.json"] = PluginPackageFixture.Sha256Of(bytes) }).ToJson());
			var store = new UiPackageStore(directories);
			var registry = new FakeApplicationRegistry();
			var options = new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create(allowBrowserCode: true));
			var api = new ApplicationsController(registry, Substitute.For<IHttpHandlerCommandDispatcher>(), options, NullLogger<ApplicationsController>.Instance, store)
			{ ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
			foreach (var (slug, room) in new[] { ("east", 10), ("west", 20) })
			{
				using var config = JsonDocument.Parse($"{{\"rooms\":[{room}]}}");
				await Assert.That(await api.Upsert(new(slug, slug, null, "Page", "", null, null, "Player", null, [], 0,
					RenderKind: ApplicationRenderKind.Hosted, DefinitionId: "graph-proof", Config: config.RootElement))).IsTypeOf<OkObjectResult>();
			}
			var host = new HostedApplicationsController(store, registry, options)
			{ ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Player")], "test")) } } };
			var east = (OkObjectResult)await host.Bootstrap("east", default);
			var west = (OkObjectResult)await host.Bootstrap("west", default);
			await Assert.That(JsonSerializer.SerializeToElement(east.Value).GetProperty("config").GetProperty("rooms")[0].GetInt32()).IsEqualTo(10);
			await Assert.That(JsonSerializer.SerializeToElement(west.Value).GetProperty("config").GetProperty("rooms")[0].GetInt32()).IsEqualTo(20);
			var metadata = (ApplicationsController.ApplicationDto)((OkObjectResult)(await api.Get("east")).Result!).Value!;
			await Assert.That(metadata!.Config).IsNull();
			host.HttpContext.User = new(new ClaimsIdentity([], "test"));
			await Assert.That(await host.Bootstrap("east", default)).IsTypeOf<ForbidResult>();
		}
		finally { PluginPackageFixture.Delete(directories); }
	}
}
