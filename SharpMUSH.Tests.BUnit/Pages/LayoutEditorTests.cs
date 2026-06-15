using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Pages;

file sealed class EditorStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>Returns an empty JSON array for any request (the live preview's widgets degrade gracefully).</summary>
file sealed class EmptyArrayHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("[]", Encoding.UTF8, "application/json")
		});
}

/// <summary>
/// Smoke tests for the drag-and-drop layout editor: the widget palette is filtered to widgets whose
/// allowed zones overlap the scope's zones, and the scope's zones are rendered as drop targets.
/// </summary>
public class LayoutEditorTests : BunitContext
{
	private ILayoutService _layout = default!;

	public LayoutEditorTests()
	{
		var registry = new WidgetRegistry();
		registry.Register(new WikiIndexWidgetDescriptor());          // MainContent
		registry.Register(new QuickLinksWidgetDescriptor());         // TopBar/sidebars/footer (no MainContent)
		registry.Register(new CharacterGalleryWidgetDescriptor());   // MainContent/RightSidebar

		// wiki-index scope exposes only MainContent.
		var layout = new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WikiIndex", 0, null)]
			},
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false));

		_layout = Substitute.For<ILayoutService>();
		_layout.GetLayoutAsync(LayoutScopes.WikiIndex).Returns(Task.FromResult(layout));

		var apiClient = new HttpClient(new EmptyArrayHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		// Rendering the component directly bypasses the router's [Authorize] gate, so no auth setup is needed.
		// The live preview renders real widgets, so register the services those widgets inject.
		Services
			.AddMudServices()
			.AddSingleton<IWidgetRegistry>(registry)
			.AddSingleton(_layout)
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EditorStubLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task PaletteFiltersByScopeZones_AndRendersZones()
	{
		var cut = Render<SharpMUSH.Client.Pages.Admin.Layout.LayoutEditor>(p => p
			.Add(x => x.Scope, LayoutScopes.WikiIndex));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Wiki Index"))
				throw new InvalidOperationException("editor not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		// MainContent-capable widgets are offered in the palette.
		await Assert.That(markup).Contains("Wiki Index");
		await Assert.That(markup).Contains("Gallery");
		// QuickLinks has no MainContent zone, so it is filtered out of this scope's palette.
		await Assert.That(markup).DoesNotContain("Quick Links");
		// The scope's single zone is rendered as a drop target.
		await Assert.That(markup).Contains("MainContent");
	}
}
