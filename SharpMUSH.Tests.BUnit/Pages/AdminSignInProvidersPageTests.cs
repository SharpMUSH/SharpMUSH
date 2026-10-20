using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Answers the staff sign-in services API from <see cref="Providers"/>, recording each save.</summary>
internal sealed class SignInProvidersApiHandler : HttpMessageHandler
{
	public List<SignInProviderSettings> Providers { get; init; } = [];
	public List<(string Id, JsonElement Body)> Saved { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		const string root = "api/admin/sign-in-providers";
		if (request.Method == HttpMethod.Get && path == root)
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Providers) };
		if (request.Method == HttpMethod.Get && path.StartsWith($"{root}/redirect-uri/", StringComparison.Ordinal))
		{
			var id = path[$"{root}/redirect-uri/".Length..];
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new SignInProviderRedirect($"https://game.example/api/auth/external/{id}/callback"))
			};
		}
		if (request.Method == HttpMethod.Put && path.StartsWith($"{root}/", StringComparison.Ordinal))
		{
			var id = path[(root.Length + 1)..];
			Saved.Add((id, await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken)));
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Providers.FirstOrDefault(p => p.Id == id) ?? Provider(id)) };
		}

		return new HttpResponseMessage(HttpStatusCode.NotFound);
	}

	public static SignInProviderSettings Provider(string id, bool ready = true, bool enabled = true, bool secretSet = true) =>
		new(id, ExternalSignInKinds.Discord, "Discord", "client-1", enabled, secretSet, ready,
			$"https://game.example/api/auth/external/{id}/callback", $"signin.{id}.secret",
			new SignInProviderEndpoints("https://discord.com/oauth2/authorize", "https://discord.com/api/oauth2/token",
				"https://discord.com/api/users/@me", "identify email", "id", "global_name username", "email", "verified"));
}

/// <summary>
/// The staff page for sign-in services: what each one's state is, the address to register with the service, and
/// that a client secret is sent only when typed (left empty, the stored one stays).
/// </summary>
public class AdminSignInProvidersPageTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> ownedHttpClients = [];

	private SignInProvidersApiHandler Seed(SignInProvidersApiHandler handler)
	{
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<SignInProvidersService>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		return handler;
	}

	private IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> RenderPage() => Render(builder =>
	{
		builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
		builder.CloseComponent();
		builder.OpenComponent<SharpMUSH.Client.Pages.Admin.SignInProviders>(1);
		builder.CloseComponent();
	});

	[Test]
	public async Task ListsEachService_WithWhatItStillNeeds()
	{
		Seed(new SignInProvidersApiHandler
		{
			Providers = [SignInProvidersApiHandler.Provider("discord"), SignInProvidersApiHandler.Provider("discord2", ready: false, secretSet: false)]
		});

		var cut = RenderPage();
		cut.WaitForAssertion(() => cut.Find(".signin-row[data-provider='discord2']"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".signin-row[data-provider='discord']").TextContent).Contains("AdmSignInReady");
		await Assert.That(cut.Find(".signin-row[data-provider='discord2']").TextContent).Contains("AdmSignInNoSecret");
		// The form starts on the first ready-made service not set up yet, with the address to register with it.
		cut.WaitForAssertion(() => cut.Find(".signin-redirect-uri").TextContent.Contains("/google/"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".signin-redirect-uri").TextContent)
			.IsEqualTo("https://game.example/api/auth/external/google/callback");
	}

	[Test]
	public async Task Editing_KeepsTheStoredSecret_WhenNoneIsTyped()
	{
		var handler = Seed(new SignInProvidersApiHandler { Providers = [SignInProvidersApiHandler.Provider("discord")] });

		var cut = RenderPage();
		cut.WaitForAssertion(() => cut.Find("button[aria-label='AdmSignInEdit']"), TimeSpan.FromSeconds(5));
		await cut.Find("button[aria-label='AdmSignInEdit']").ClickAsync();
		await Assert.That(cut.Markup).Contains("AdmSignInSecretKeep");

		await cut.Find("button.signin-save").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (handler.Saved.Count == 0) throw new InvalidOperationException("not saved yet");
		}, TimeSpan.FromSeconds(5));

		var (id, body) = handler.Saved.Single();
		await Assert.That(id).IsEqualTo("discord");
		await Assert.That(body.GetProperty("kind").GetString()).IsEqualTo(ExternalSignInKinds.Discord);
		await Assert.That(body.GetProperty("clientId").GetString()).IsEqualTo("client-1");
		await Assert.That(body.GetProperty("clientSecret").ValueKind).IsEqualTo(JsonValueKind.Null);
		await Assert.That(body.GetProperty("endpoints").ValueKind).IsEqualTo(JsonValueKind.Null);
	}

	/// <summary>Adding a service under an id already set up would replace that one, so the form refuses it.</summary>
	[Test]
	public async Task ANewService_CannotTakeAnIdAlreadySetUp()
	{
		Seed(new SignInProvidersApiHandler { Providers = [SignInProvidersApiHandler.Provider("discord")] });
		var cut = RenderPage();
		cut.WaitForAssertion(() => cut.Find(".signin-row"), TimeSpan.FromSeconds(5));

		await cut.Find(".signin-field--mono input").ChangeAsync("discord");
		await cut.FindAll("input").Single(i => i.GetAttribute("type") == "password").ChangeAsync("secret");
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AdmSignInIdTaken")) throw new InvalidOperationException("the id is not refused yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("button.signin-save").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task ANewService_CannotBeSavedWithoutItsSecret()
	{
		Seed(new SignInProvidersApiHandler());
		var cut = RenderPage();
		cut.WaitForAssertion(() => cut.Find(".signin-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("button.signin-save").HasAttribute("disabled")).IsTrue();
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in ownedHttpClients) client.Dispose();
		await base.DisposeAsync();
	}
}
