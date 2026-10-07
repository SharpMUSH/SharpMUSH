using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Answers the gallery read with one image, once the test lets it.</summary>
file sealed class HeldGalleryHandler(Task release) : HttpMessageHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		await release;
		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""[{"assetId":"old","fileName":"old.png","url":"/files/old","caption":null,"order":0,"isIcon":true,"isBanner":false}]""",
				Encoding.UTF8, "application/json")
		};
	}
}

/// <summary>
/// The profile page's Change banner and Change avatar hand the gallery widget what the server answered.
/// A read the widget began before that is older, and must not replace it: the widget's next write sends
/// its list back whole, and would drop the image the page just added.
/// </summary>
public class GalleryPageWriteTests : TrackingBunitContext
{
	private readonly TaskCompletionSource _release = new();

	public GalleryPageWriteTests()
	{
		var apiClient = Track(new HttpClient(new HeldGalleryHandler(_release.Task)) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new GalleryService(sp.GetRequiredService<IHttpClientFactory>()))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task AReadBegunBeforeThePagesWrite_DoesNotReplaceIt()
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", true))
			.AddChildContent<CharacterGalleryWidget>());

		IReadOnlyList<GalleryEntry> written =
		[
			new("old", "old.png", "/files/old", null, 0, true),
			new("new", "new.png", "/files/new", null, 1, false, IsBanner: true),
		];
		cut.Render(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", true, Gallery: written))
			.AddChildContent<CharacterGalleryWidget>());
		_release.SetResult();
		await Task.Delay(100);

		cut.WaitForAssertion(() => cut.Find(".gallery-small-tile img[src$='/files/new']"), TimeSpan.FromSeconds(5));
	}
}
