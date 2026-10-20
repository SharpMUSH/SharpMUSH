using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Pages;

public class SetupIdentityTests : BunitContext
{
	private sealed class Handler : HttpMessageHandler
	{
		public int Writes;
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var result = new OnboardingSettings("Imported world", ["Fantasy"], ["High Fantasy"], "An existing introduction", "", "", "", "chibi", "");
			if (request.Method == HttpMethod.Put) { Writes++; result = (await request.Content!.ReadFromJsonAsync<OnboardingSettings>(cancellationToken))!; }
			return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
		}
	}
	private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://example.test/") };
	}
	[Test]
	public async Task ImportedIdentityIsPrefilledAndOnlySavedExplicitly()
	{
		using var handler = new Handler();
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = Render<GameIdentityForm>();
		form.WaitForElement("#guide-game-name");
		await Assert.That(form.Find("#guide-game-name").GetAttribute("value")).IsEqualTo("Imported world");
		await Assert.That(handler.Writes).IsEqualTo(0);
		await form.Find("#guide-game-name").ChangeAsync("My new world");
		await Assert.That(form.Find(".identity-preview").TextContent.Contains("My new world", StringComparison.Ordinal)).IsTrue();
		await Assert.That(handler.Writes).IsEqualTo(0);
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Writes).IsEqualTo(1);
		await Assert.That(form.Markup.Contains("Saved", StringComparison.Ordinal)).IsTrue();
		await form.Find("#guide-introduction").ChangeAsync("Unsaved introduction");
		await Assert.That(form.FindAll("[role=status]").Any(node => node.TextContent.Contains("Saved", StringComparison.Ordinal))).IsFalse();
		await Assert.That(handler.Writes).IsEqualTo(1);
	}
}
