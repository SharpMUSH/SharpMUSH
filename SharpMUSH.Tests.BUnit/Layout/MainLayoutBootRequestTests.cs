using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// MainLayout's initialisation used to await its requests one after another — setup status, then the
/// global layout, then server info — so the shell's zones and the Play tab waited on the sum of the
/// round trips. None depends on another, so they go out together now.
/// </summary>
public class MainLayoutBootRequestTests : TrackingBunitContext
{
	[Test]
	public async Task TheGlobalLayout_IsRequested_WhileTheSetupStatusIsStillOut()
	{
		// Anonymous visitor: the setup-status check is asked (a debug identity would skip it). Hold its
		// answer, and see what else MainLayout has asked for meanwhile.
		using var handler = new GatedHttpHandler(
			request => request.RequestUri!.AbsolutePath == "/api/setup/status"
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { needsSetup = false }) }
				: new HttpResponseMessage(HttpStatusCode.NotFound),
			hold: request => request.RequestUri!.AbsolutePath == "/api/setup/status");
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(apiClient);

		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(Arg.Any<string>()).Returns(Task.FromResult(new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>(),
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false))));
		var serverInfo = Substitute.For<ServerInfoService>(factory);
		serverInfo.GuestLoginsEnabledAsync().Returns(Task.FromResult(true));
		serverInfo.GameNameAsync().Returns(Task.FromResult("SharpMUSH"));

		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		Services.AddSingleton(factory);
		Services.AddSingleton(layouts);
		Services.AddSingleton<IWidgetRegistry>(new WidgetRegistry());
		Services.AddSingleton(serverInfo);
		Services.AddSingleton(new ApplicationCatalog([]));
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []));
		Services.AddSingleton(Substitute.For<IConnectionStateService>());
		Services.AddSingleton<TerminalResumeStore>();
		Services.AddSingleton<CharacterSwitchService>();
		Services.AddSingleton<TerminalLoginService>();
		var terminal = new TerminalServiceHost(() => Substitute.For<ITerminalService>());
		Services.AddSingleton(terminal);
		Services.AddSingleton<ITerminalService>(terminal);
		var playTerminal = new PlayTerminalServiceHost(() => Substitute.For<IPlayTerminalService>());
		Services.AddSingleton(playTerminal);
		Services.AddSingleton<IPlayTerminalService>(playTerminal);
		var hostEnv = Substitute.For<Microsoft.AspNetCore.Components.WebAssembly.Hosting.IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);
		JSInterop.Mode = JSRuntimeMode.Loose;
		AddAuthorization();

		var cut = Render<MainLayout>();
		cut.WaitForState(() => handler.CallsTo("/api/setup/status") == 1, TimeSpan.FromSeconds(5));

		await layouts.Received().GetLayoutAsync(LayoutScopes.Global);
		await serverInfo.Received().GuestLoginsEnabledAsync();

		handler.Release();
	}
}
