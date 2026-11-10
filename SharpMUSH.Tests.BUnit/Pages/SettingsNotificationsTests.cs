using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class SettingsNotificationsTests : TrackingBunitContext
{
	private sealed class DeviceApi : HttpMessageHandler
	{
		public bool Fail { get; set; } = true;
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(Fail
				? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<PushDeviceResponse>()) });
	}

	[Test]
	public async Task Failed_devices_are_unknown_until_retry_confirms_an_empty_list()
	{
		var api = new DeviceApi();
		var http = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		var connection = new ConnectionStateService(Substitute.For<IGameHubConnectionFactory>(), NullLogger<ConnectionStateService>.Instance);
		Services.AddMudServices().AddEchoLocalizer()
			.AddSingleton(sp => new PushInterop(JSInterop.JSRuntime, connection, auth, sp.GetRequiredService<NavigationManager>()))
			.AddSingleton(sp => new AccountPushService(factory, sp.GetRequiredService<PushInterop>())).AddSingleton(new ApplicationCatalog([]));
		ComponentFactories.AddStub<GameName>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<PushBrowserState>("SharpMUSH.Push.state").SetResult(new(true, "granted", "endpoint", false, false, false));
		var host = Render<MudHarness>(p => p.AddChildContent<SettingsNotifications>());
		host.WaitForAssertion(() => host.Find(".settings-notify-device-error"));
		await Assert.That(host.Markup).DoesNotContain("NotifyNoDevices");
		await Assert.That(host.FindAll("[data-state=off]")).IsEmpty();
		api.Fail = false;
		await host.Find(".settings-notify-retry").ClickAsync();
		await Assert.That(host.FindAll(".settings-notify-device-error")).IsEmpty();
		await Assert.That(host.Markup).Contains("NotifyNoDevices");
		Services.GetRequiredService<PushInterop>().Dispose();
		await connection.DisposeAsync();
	}
}
