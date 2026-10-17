using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models.Portal.Discord;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Read-only example fixtures for the illustrated guide, rendered by the real localized components.</summary>
public class DiscordGuideUiTests : TrackingBunitContext
{
	public DiscordGuideUiTests()
	{
		var client = Track(new HttpClient(new GuideApi()) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices().AddSingleton(factory).AddLogging()
			.AddSingleton(TimeProvider.System).AddSingleton<DiscordAdminService>()
			.AddSingleton<RoleRegistryClient>().AddSingleton<SecretsService>()
			.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create())
			.AddSingleton<ServerInfoService>(new StubServerInfoService(true, features: [GameFeatures.Discord]));
		var theme = Substitute.For<IThemeService>();
		theme.Current.Returns(ThemeResolver.Resolve(BuiltInThemes.Phosphor));
		Services.AddSingleton(theme);
		AddAuthorization().SetAuthorized("example administrator");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task ConnectedExampleShowsEverySetupStepAndBothChannelAccessModes()
	{
		using var culture = CultureScope.For("en");
		var cut = Render<ThemeProvider>(p => p.AddChildContent<MudHarness>(h => h.AddChildContent<AdminDiscord>()));
		cut.WaitForAssertion(() => cut.Find(".adm-discord-connection"));
		await Assert.That(cut.FindAll(".adm-discord-step--done").Count).IsEqualTo(5);
		await Assert.That(cut.FindAll(".adm-discord-step:not(.adm-discord-step--done)")).IsEmpty();
		await Assert.That(cut.FindAll(".adm-discord-unrestrict").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".adm-discord-restrict").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".adm-discord-token").GetAttribute("href")).IsEqualTo("/admin/config/secrets#config-option-discord.token");
		await CaptureAsync("discord-admin.html", cut.Markup);
	}

	[Test]
	public async Task TokenExampleUsesTheActualWriteOnlySecretFormWithoutAnyValue()
	{
		using var culture = CultureScope.For("en");
		var cut = Render<ThemeProvider>(p => p.AddChildContent<Secrets>());
		cut.WaitForAssertion(() => cut.Find(".secret-rows"));
		await cut.Find(".secret-field--name input").InputAsync("discord.token");
		var value = cut.Find("input[type=password]");
		await Assert.That(value.GetAttribute("value") ?? "").IsEmpty();
		await Assert.That(value.GetAttribute("autocomplete")).IsEqualTo("new-password");
		await Assert.That(cut.Find(".secret-save").HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.Find(".secret-name").TextContent).IsEqualTo("discord.token");
		await CaptureAsync("discord-secret.html", cut.Markup);
	}

	private static async Task CaptureAsync(string name, string markup)
	{
		if (Environment.GetEnvironmentVariable("SHARPMUSH_DISCORD_GUIDE_CAPTURE_PATH") is not { Length: > 0 } path) return;
		Directory.CreateDirectory(path);
		await File.WriteAllTextAsync(Path.Combine(path, name), markup);
		await File.WriteAllTextAsync(Path.Combine(path, "theme-parts.json"), System.Text.Json.JsonSerializer.Serialize(ThemeResolver.Resolve(BuiltInThemes.Phosphor).Parts));
	}

	private sealed class GuideApi : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Guide fixtures permit reads only.");
			var at = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
			return Task.FromResult(request.RequestUri!.AbsolutePath switch
			{
				"/api/discord/admin/connection" => Json(new DiscordConnectionView("connected", "Example Game Bot", "https://discord.com/oauth2/authorize", 1,
					[ new("token", "Bot token", true, null, null), new("intents", "Privileged intents", true, null, null),
						new("private", "Only you can add the bot", true, null, null), new("gateway", "Connected to Discord", true, null, null),
						new("server", "In a server", true, null, null) ])),
				"/api/discord/admin/channels" => Json(new DiscordChannelView[]
				{
					new("1234567890123456789", "roleplay", "Example Game", "discord/1234567890123456789", "ok", 0, null, [], at),
					new("1234567890123456790", "newbie-help", "Example Game", "discord/1234567890123456790", "ok", 0, null, [], at, true),
				}),
				"/api/discord/admin/channels/available" => Json(Array.Empty<DiscordTextChannelView>()),
				"/api/discord/admin/roles" => Json(new DiscordRolesView([], [])),
				"/api/discord/admin/members" => Json(Array.Empty<DiscordMemberView>()),
				"/api/roles" => Json(Array.Empty<object>()),
				"/api/secrets" => Json(new SecretStatus[] { new("discord.token", "The Discord bot's token.", SecretSource.Unset, null, false, null, null, "discord.admin", true) }),
				_ => new HttpResponseMessage(HttpStatusCode.NotFound),
			});
		}

		private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
	}
}
