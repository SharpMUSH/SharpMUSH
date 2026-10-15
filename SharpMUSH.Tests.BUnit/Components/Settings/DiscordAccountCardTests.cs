using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Settings;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Discord;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Settings;

/// <summary>The account page's Discord card: who the account is linked to, and unlinking it.</summary>
public class DiscordAccountCardTests : TrackingBunitContext
{
	private sealed class DiscordApi(DiscordAccountView view) : HttpMessageHandler
	{
		public int Unlinks { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath != "/api/discord/account")
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}

			if (request.Method == HttpMethod.Delete)
			{
				Unlinks++;
				view = new DiscordAccountView(null, null);
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(view) });
		}
	}

	private DiscordApi Serve(DiscordAccountView view)
	{
		var api = new DiscordApi(view);
		var http = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton<DiscordAccountService>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		return api;
	}

	[Test]
	public async Task ALinkedAccount_ShowsTheDiscordUser_AndUnlinks()
	{
		var api = Serve(new DiscordAccountView("ann", new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero)));

		var cut = Render<DiscordAccountCard>();
		cut.WaitForAssertion(() => cut.Find(".acct-value").TextContent.Contains("ann"));
		await cut.Find("button").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".acct-empty"));
		await Assert.That(api.Unlinks).IsEqualTo(1);
		await Assert.That(cut.Find(".acct-empty").TextContent).Contains("DiscordAccountNotLinked");
	}

	[Test]
	public async Task AnUnlinkedAccount_SaysHowToLink()
	{
		Serve(new DiscordAccountView(null, null));

		var cut = Render<DiscordAccountCard>();

		cut.WaitForAssertion(() => cut.Find(".acct-empty"));
		await Assert.That(cut.FindAll("button").Count).IsEqualTo(0);
	}
}
