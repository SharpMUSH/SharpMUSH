using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

file sealed class ByMethodHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
		Task.FromResult(respond(request));
}

/// <summary>
/// When the gallery API refuses, the widget says what the server said. <see cref="GalleryService"/>
/// used to answer an empty list or <see langword="null"/>: a gallery the server could not read looked
/// like one with no images, and a refused delete or icon change left the page as it was with nothing
/// said at all.
/// </summary>
public class GalleryRefusalTests : TrackingBunitContext
{
	private const string Refusal = "You do not control Gandalf.";

	private void Arrange(bool isIcon, Func<HttpMethod, HttpStatusCode> status)
	{
		var gallery = $$"""
			[{"assetId":"A1","fileName":"portrait.png","url":"/files/A1","caption":null,"order":0,"isIcon":{{(isIcon ? "true" : "false")}}}]
			""";

		var handler = new ByMethodHandler(request =>
		{
			var code = status(request.Method);
			return new HttpResponseMessage(code)
			{
				Content = new StringContent(
					code == HttpStatusCode.OK ? gallery : $$"""{"error":"{{Refusal}}"}""",
					Encoding.UTF8, "application/json")
			};
		});
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new GalleryService(sp.GetRequiredService<IHttpClientFactory>()))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<CharacterGalleryWidget> RenderLoaded()
	{
		var cut = Render<CharacterGalleryWidget>(p => p
			.Add(w => w.CharacterName, "Gandalf")
			.Add(w => w.CanEdit, true));
		cut.WaitForAssertion(() => cut.Find(".gallery-thumb button"), TimeSpan.FromSeconds(5));
		return cut;
	}

	private async Task<Snackbar> SingleSnackbar(IRenderedComponent<CharacterGalleryWidget> cut)
	{
		var snackbar = Services.GetRequiredService<ISnackbar>();
		cut.WaitForAssertion(() =>
		{
			if (!snackbar.ShownSnackbars.Any())
				throw new InvalidOperationException("no snackbar yet");
		}, TimeSpan.FromSeconds(5));

		var shown = snackbar.ShownSnackbars.Single();
		await Assert.That(shown.Severity).IsEqualTo(Severity.Error);
		return shown;
	}

	[Test]
	public async Task AFailedGalleryReadIsNotAnEmptyGallery()
	{
		Arrange(isIcon: false, _ => HttpStatusCode.ServiceUnavailable);

		var cut = Render<CharacterGalleryWidget>(p => p.Add(w => w.CharacterName, "Gandalf"));
		cut.WaitForAssertion(() => cut.Find(".character-gallery-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".character-gallery-error").TextContent.Trim()).IsEqualTo(Refusal);
		await Assert.That(cut.Markup).DoesNotContain("GalleryEmpty");
	}

	[Test]
	public async Task ARefusedDeleteSaysWhy_AndKeepsTheImage()
	{
		// An icon already, so the only button on the thumbnail is delete.
		Arrange(isIcon: true, method => method == HttpMethod.Delete ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
		var cut = RenderLoaded();

		cut.Find(".gallery-thumb button").Click();

		var shown = await SingleSnackbar(cut);
		await Assert.That(shown.Message).IsEqualTo($"GalleryDeleteFailed({Refusal})");
		await Assert.That(cut.Markup).Contains("portrait.png");
	}

	[Test]
	public async Task ARefusedIconChangeSaysWhy()
	{
		// Not the icon, so the first button on the thumbnail is "set as icon".
		Arrange(isIcon: false, method => method == HttpMethod.Put ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
		var cut = RenderLoaded();

		cut.Find(".gallery-thumb button").Click();

		var shown = await SingleSnackbar(cut);
		await Assert.That(shown.Message).IsEqualTo($"GallerySetIconFailed({Refusal})");
	}
}
