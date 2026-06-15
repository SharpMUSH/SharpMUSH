using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

file sealed class CtxStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>Returns an empty gallery for any character.</summary>
file sealed class EmptyGalleryHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("[]", Encoding.UTF8, "application/json")
		});
}

/// <summary>
/// Confirms a profile widget reads the cascading <see cref="ProfilePageContext"/>: the gallery's edit
/// controls (the file-upload input) appear only when the context grants edit rights — the mechanism by
/// which the profile page hands the route's character and edit rights to zone-placed widgets.
/// </summary>
public class ProfilePageContextTests : BunitContext
{
	public ProfilePageContextTests()
	{
		var apiClient = new HttpClient(new EmptyGalleryHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new GalleryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<GalleryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, CtxStubLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<CascadingValue<ProfilePageContext>> RenderWithContext(bool canEdit)
		=> Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", canEdit))
			.Add(x => x.IsFixed, true)
			.AddChildContent<CharacterGalleryWidget>());

	[TUnit.Core.Test]
	public async Task EditContext_ShowsUploadControl()
	{
		var cut = RenderWithContext(canEdit: true);
		cut.WaitForState(() => !cut.Markup.Contains("mud-progress"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).Contains("type=\"file\"");
	}

	[TUnit.Core.Test]
	public async Task ReadOnlyContext_HidesUploadControl()
	{
		var cut = RenderWithContext(canEdit: false);
		cut.WaitForState(() => !cut.Markup.Contains("mud-progress"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).DoesNotContain("type=\"file\"");
	}
}
