using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled notifications package, installed from the catalogue: +notify and the portal page's routes, read and
/// saved the way the portal does. Not in parallel: each test installs the package, which attaches to the one
/// HTTP and event handler.
/// </summary>
[NotInParallel]
public class NotificationsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> God(string command)
	{
		var said = (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message?.ToPlainText()?.Trim() ?? string.Empty;
		await WebAppFactoryArg.QueueBarrierAsync();
		return said;
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	/// <summary>The route's reply, run straight through the HTTP handler and not waiting on the queue after it.</summary>
	private async Task<JsonElement> Http(string method, string path, string body, TestIsolationHelpers.TestPlayer viewer)
	{
		var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();
		var who = DBRef.Parse(await God($"think objid(#{viewer.DbRef.Number})"));
		var response = (await dispatcher.DispatchAsync(method, path, body, [], IHttpHandlerCommandDispatcher.UnknownAddress, who))
			.Expect<HttpHandlerResult>();
		await Assert.That(response.Status).IsEqualTo(200).Because(response.Body);
		await Assert.That(response.Body.TrimStart()).StartsWith("{").Because("the reply is JSON alone: " + response.Body);
		return JsonDocument.Parse(response.Body).RootElement.Clone();
	}

	private async Task InstallAsync()
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "notifications", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("notifications must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private Task UninstallAsync() => Installer.UninstallAsync("notifications", force: true, CancellationToken.None);

	/// <summary>The value of the switch <paramref name="key"/> on the portal page, as the viewer has it.</summary>
	private static bool Switch(JsonElement schema, string key) =>
		schema.GetProperty("pages")[0].GetProperty("sections").EnumerateArray()
			.SelectMany(s => s.GetProperty("elements").EnumerateArray())
			.Single(e => e.GetProperty("key").GetString() == key).GetProperty("default").GetBoolean();

	[Test]
	public async Task NotifyListsAndChangesAPlayersSettings()
	{
		try
		{
			await InstallAsync();
			var ann = await PlayerAsync("NotifyAnn");

			var listed = await As(ann, "+notify");
			await Assert.That(listed).Contains("Notifications");
			await Assert.That(listed).Contains("A page sent to you");
			await Assert.That(listed).Contains("+notify <setting>=on or off changes one.");

			await Assert.That(await As(ann, "+notify page")).Contains("+notify <setting>=on or off. +notify lists the settings.");
			await Assert.That(await As(ann, "+notify bogus=on")).Contains("There is no setting called 'bogus'. +notify lists them.");
			await Assert.That(await As(ann, "+notify page=maybe")).Contains("Say on or off: +notify page=on");
			await Assert.That(await As(ann, "+notify page=off")).Contains("page is off: A page sent to you.");
			await Assert.That(await As(ann, "+notify preview=on")).Contains("preview is on: Show the message, not only who sent it.");

			var schema = await Http("GET", "/notifications/schema", "", ann);
			await Assert.That(Switch(schema, "page")).IsFalse();
			await Assert.That(Switch(schema, "mail")).IsTrue();
			await Assert.That(Switch(schema, "preview")).IsTrue();

			// The choices are the player's own: kept on the player, not on the Notifications object.
			await Assert.That(await God($"think get(#{ann.DbRef.Number}/NOTIFY`PAGE)/[get(#{ann.DbRef.Number}/NOTIFY`PREVIEW)]")).IsEqualTo("0/1");

			// Another player keeps the defaults.
			var bo = await PlayerAsync("NotifyBo");
			var theirs = await Http("GET", "/notifications/schema", "", bo);
			await Assert.That(Switch(theirs, "page")).IsTrue();
			await Assert.That(Switch(theirs, "preview")).IsFalse();
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>
	/// The save route keeps the portal's switches on the player, and the page's next read, queued after the write,
	/// shows them. The reply is the JSON alone, with none of the writes' reports in it.
	/// </summary>
	[Test]
	public async Task TheSaveRouteKeepsTheChoicesOnThePlayer()
	{
		try
		{
			await InstallAsync();
			var ann = await PlayerAsync("NotifySave");

			var saved = await Http("POST", "/notifications/save", JsonSerializer.Serialize(new { page = false, mail = true, preview = true }), ann);
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await God($"think get(#{ann.DbRef.Number}/NOTIFY`PAGE)/[get(#{ann.DbRef.Number}/NOTIFY`MAIL)]")).IsEqualTo("0/1");
			await Assert.That(saved.GetProperty("ok").GetBoolean()).IsTrue();
			await Assert.That(saved.GetProperty("message").GetString()).IsEqualTo("Saved.");

			var schema = await Http("GET", "/notifications/schema", "", ann);
			await Assert.That(Switch(schema, "page")).IsFalse();
			await Assert.That(Switch(schema, "mail")).IsTrue();
			await Assert.That(Switch(schema, "preview")).IsTrue();
			await WebAppFactoryArg.QueueBarrierAsync();

			// Nothing the route wrote reached the reply.
			await Assert.That(saved.EnumerateObject().Select(p => p.Name)).IsEquivalentTo(new[] { "ok", "message" });
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
