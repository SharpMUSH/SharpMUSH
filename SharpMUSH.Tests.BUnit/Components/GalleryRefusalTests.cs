using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
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

	private async Task<IRenderedComponent<CharacterGalleryWidget>> RenderLoaded()
	{
		var cut = Render<CharacterGalleryWidget>(p => p
			.Add(w => w.CharacterName, "Gandalf")
			.Add(w => w.CanEdit, true));
		cut.WaitForAssertion(() => cut.Find("button.gallery-view-all"), TimeSpan.FromSeconds(5));
		await cut.Find("button.gallery-view-all").ClickAsync();
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
		Arrange(isIcon: true, method => method == HttpMethod.Delete ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
		var cut = await RenderLoaded();

		await cut.Find(".kit-viewer-actions button.gallery-delete").ClickAsync();

		var shown = await SingleSnackbar(cut);
		await Assert.That(shown.Message).IsEqualTo($"GalleryDeleteFailed({Refusal})");
		await Assert.That(cut.Markup).Contains("portrait.png");
	}

	[Test]
	public async Task ARefusedIconChangeSaysWhy()
	{
		Arrange(isIcon: false, method => method == HttpMethod.Put ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
		var cut = await RenderLoaded();

		await cut.Find(".kit-viewer-actions button.gallery-make-icon").ClickAsync();

		var shown = await SingleSnackbar(cut);
		await Assert.That(shown.Message).IsEqualTo($"GallerySetIconFailed({Refusal})");
	}

	/// <summary>
	/// A browser that cannot name a file's type reports <c>""</c>. Building the part's Content-Type from
	/// that threw out of the upload handler; the upload now goes, and the server's refusal comes back.
	/// </summary>
	[Test]
	public async Task AnUploadWithNoContentTypeReachesTheServer()
	{
		Arrange(isIcon: false, method => method == HttpMethod.Post ? HttpStatusCode.BadRequest : HttpStatusCode.OK);

		var file = Substitute.For<IBrowserFile>();
		file.Name.Returns("portrait");
		file.ContentType.Returns(string.Empty);
		file.Size.Returns(3);
		file.OpenReadStream(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream([1, 2, 3]));

		var result = await Services.GetRequiredService<GalleryService>().UploadAsync("Gandalf", file);

		await Assert.That(result.Expect<ApiFailure>().Message).IsEqualTo(Refusal);
	}
}
