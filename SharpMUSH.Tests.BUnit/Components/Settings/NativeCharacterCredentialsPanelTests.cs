using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Settings;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Settings;

public class NativeCharacterCredentialsPanelTests : TrackingBunitContext
{
	private const string Secret = "nc1_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
	private sealed class Api(bool expired) : HttpMessageHandler
	{
		public int Mints, Revocations;
		private bool _pending;
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var expiry = DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 2);
			object? payload = null;
			if (request.Method == HttpMethod.Post)
			{
				Mints++; _pending = true;
				payload = new NativeLoginSecretResponse("opaque", Secret, 10, 1000, expiry);
			}
			else if (request.Method == HttpMethod.Get)
				payload = _pending ? new[] { new NativeLoginStatusResponse("opaque", 10, 1000, expiry) } : Array.Empty<NativeLoginStatusResponse>();
			else { Revocations++; _pending = false; }
			return Task.FromResult(new HttpResponseMessage(payload is null ? HttpStatusCode.NoContent : HttpStatusCode.OK)
			{
				Content = payload is null ? null : JsonContent.Create(payload)
			});
		}
	}

	private Api Serve(bool expired = false)
	{
		var api = new Api(expired);
		var http = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient("api").Returns(http);
		Services.AddMudServices().AddSingleton(factory).AddSingleton<NativeCharacterCredentialsClient>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		ComponentFactories.AddStub<RecentAccountProofPrompt>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		return api;
	}

	private static Task Prove(IRenderedComponent<NativeCharacterCredentialsPanel> cut)
		=> cut.InvokeAsync(() => cut.FindComponent<Stub<RecentAccountProofPrompt>>().Instance.Parameters.Get(p => p.OnProved).InvokeAsync("one-use-proof"));

	[Test]
	public async Task ProofRequiresAnExplicitGenerateActionAndRevocationClearsTheDisplayedSecret()
	{
		var api = Serve();
		var cut = Render<NativeCharacterCredentialsPanel>(p => p.Add(x => x.Character, new(10, 1000, "Character", "")));
		await Prove(cut);
		await Assert.That(api.Mints).IsEqualTo(0);
		await Assert.That(cut.Markup).DoesNotContain("one-use-proof");
		await cut.FindAll("button").Single(b => b.TextContent.Contains("AuthNativeGenerate")).ClickAsync();
		await Assert.That(api.Mints).IsEqualTo(1);
		await Assert.That(cut.Find("code").TextContent).IsEqualTo("connect token " + Secret);
		await cut.FindAll("button").Single(b => b.TextContent.Contains("AuthNativeRevoke")).ClickAsync();
		await Assert.That(api.Revocations).IsEqualTo(1);
		await Assert.That(cut.FindAll("code").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AResponseThatAlreadyExpiredIsNeverDisplayedOrCopyable()
	{
		Serve(expired: true);
		var cut = Render<NativeCharacterCredentialsPanel>(p => p.Add(x => x.Character, new(10, 1000, "Character", "")));
		await Prove(cut);
		await cut.FindAll("button").Single(b => b.TextContent.Contains("AuthNativeGenerate")).ClickAsync();
		await Assert.That(cut.FindAll("code").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).DoesNotContain(Secret);
	}
	[Test]
	public async Task RealLocalizedPanelDisplaysOneFullLengthCommandForOptionalVisualCapture()
	{
		using var culture = CultureScope.For("en");
		Serve();
		Services.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create());
		var cut = Render<NativeCharacterCredentialsPanel>(p => p.Add(x => x.Character, new(10, 1000, "Native Character", "")));
		await Prove(cut);
		await cut.FindAll("button").Single(button => button.TextContent.Contains("Generate one-use login", StringComparison.Ordinal)).ClickAsync();
		await Assert.That(Secret.Length).IsEqualTo(47);
		await Assert.That(cut.FindAll("code").Count).IsEqualTo(1);
		await Assert.That(cut.Find("code").TextContent).IsEqualTo("connect token " + Secret);
		if (Environment.GetEnvironmentVariable("SHARPMUSH_SOCIAL_UI_CAPTURE_PATH") is { Length: > 0 } capturePath)
		{
			Directory.CreateDirectory(capturePath);
			await File.WriteAllTextAsync(Path.Combine(capturePath, "native-panel.html"), cut.Markup);
		}
	}

}
