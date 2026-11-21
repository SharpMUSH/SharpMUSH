using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Answers the media library's list with no pictures.</summary>
file sealed class EmptyAssetListHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(Array.Empty<WikiAssetInfo>()) });
}

/// <summary>An <c>image</c> schema field is the portal's picture field: a preview, the address, and the media picker.</summary>
public class SchemaImageFieldTests : TrackingBunitContext
{
	private const string Picture = "/api/wiki-assets/abc/fox.svg";

	public SchemaImageFieldTests()
	{
		var client = Track(new HttpClient(new EmptyAssetListHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILogger<WikiAssetService>>(NullLogger<WikiAssetService>.Instance)
			.AddSingleton<WikiAssetService>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		var auth = AddAuthorization();
		auth.SetAuthorized("keeper");
		auth.SetPolicies("media.admin", "media.upload");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<SchemaFieldInput> RenderField(object? value, Action<object?>? changed = null) =>
		Render<SchemaFieldInput>(p => p
			.Add(x => x.Element, new SchemaElement(Kind: "field", Key: "image", Label: "Picture", Type: "image"))
			.Add(x => x.Value, value)
			.Add(x => x.ValueChanged, v => changed?.Invoke(v)));

	[TUnit.Core.Test]
	public async Task AnImageField_PreviewsTheAddressItHolds()
	{
		var cut = RenderField(Picture);

		await Assert.That(cut.Find(".cfg-image input").GetAttribute("value")).IsEqualTo(Picture);
		await Assert.That(cut.Find(".cfg-image-preview img").GetAttribute("src")).Contains("fox.svg");
	}

	[TUnit.Core.Test]
	public async Task TypingAnAddress_ChangesTheValue()
	{
		object? seen = null;
		var cut = RenderField(null, v => seen = v);

		await cut.Find(".cfg-image input").ChangeAsync(new() { Value = "https://example.com/fox.png" });

		await Assert.That(seen as string).IsEqualTo("https://example.com/fox.png");
	}

	[TUnit.Core.Test]
	public async Task AReaderWhoMayUpload_IsOfferedThePicker()
	{
		var cut = RenderField(null);

		await Assert.That(cut.FindAll("button[aria-expanded]").Count).IsEqualTo(1);
	}
}
