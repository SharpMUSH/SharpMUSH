using System.Net;
using System.Text;
using System.Text.Json;
using BlazorMonaco;
using BlazorMonaco.Editor;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Serves the two <c>api/objects</c> reads the editor needs to open a tab, for a single fixed object.
/// </summary>
file sealed class ObjectApiHandler(ObjectSummaryDto summary, IReadOnlyList<AttributeDto> attributes) : HttpMessageHandler
{
	private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		object? body = path switch
		{
			"/api/objects/8/attributes" => attributes,
			"/api/objects/8" => summary,
			// The help drawer's definition file: an empty document keeps it out of the way.
			"/data/mush-defs.json" => new { },
			_ => null,
		};

		// Built into a local and returned rather than composed inline: ownership passes to the
		// HttpClient that disposes it, and a conditional expression full of `new` reads to analysis
		// as a temporary nobody disposes.
		HttpResponseMessage response;
		if (body is null)
		{
			response = new HttpResponseMessage(HttpStatusCode.NotFound);
		}
		else
		{
			response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(JsonSerializer.Serialize(body, CamelCase), Encoding.UTF8, "application/json")
			};
		}

		return Task.FromResult(response);
	}
}

/// <summary>
/// The Softcode Editor's open-tab strip: how a tab is labelled, and that its close control
/// actually closes the tab rather than only switching to it.
/// </summary>
public class SoftcodeEditorTabsTests : BunitContext
{
	private readonly HttpClient _api;

	public SoftcodeEditorTabsTests()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(true);
		terminal.SendCommandAsync(Arg.Any<string>()).Returns(call =>
			Task.FromResult<string[]>(call.Arg<string>().Contains("hasflag")
				? ["0"]
				: ["SHARP_OBJ:8:THING:Widget"]));

		var handler = new ObjectApiHandler(
			new ObjectSummaryDto("#8", "Widget", "THING", "Wizard(#1)", []),
			[new AttributeDto("DESCRIBE", "A widget.", [])]);
		// Held in a field and disposed at teardown; disposing the client disposes the handler with it.
		_api = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(_api);

		// Rendering the page directly bypasses the router's [Authorize] gate, so no auth setup is needed.
		Services
			.AddMudServices()
			.AddSingleton(terminal)
			.AddSingleton(factory)
			.AddSingleton<ObjectApiService>()
			.AddSingleton(sp => new HelpService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")))
			.AddSingleton(sp => new MushQueryService(
				sp.GetRequiredService<ITerminalService>(), NullLogger<MushQueryService>.Instance))
			.AddEchoLocalizer();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>
	/// Opens #8/DESCRIBE in a tab and returns the render tree. The page is hosted inside the shared
	/// MudHarness because its toolbar uses MudTooltip, which needs a MudPopoverProvider alongside it.
	/// </summary>
	private async Task<IRenderedComponent<Components.MudHarness>> RenderWithOneOpenTabAsync()
	{
		var cut = Render<Components.MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.SoftcodeEditor>());

		cut.WaitForElement(".ob-item", TimeSpan.FromSeconds(5)).Click();
		cut.WaitForElement(".sc-attr-item", TimeSpan.FromSeconds(5)).Click();
		cut.WaitForElement(".sc-tab", TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".sc-tab")).Count().IsEqualTo(1);
		return cut;
	}

	[TUnit.Core.Test]
	public async Task TabIsLabelledWithTheCanonicalObjectSlashAttributeReference()
	{
		var cut = await RenderWithOneOpenTabAsync();

		var label = cut.Find(".sc-tab-label").TextContent.Trim();

		// '#8/DESCRIBE' is how softcode itself names an attribute (get(), u(), @force ...).
		// '#8&DESCRIBE' is neither that nor the '&DESCRIBE #8' set syntax it borrows the sigil from.
		await Assert.That(label).IsEqualTo("#8/DESCRIBE");
	}

	[TUnit.Core.Test]
	public async Task ClosingATabRemovesIt()
	{
		var cut = await RenderWithOneOpenTabAsync();

		cut.Find(".sc-tab-close").Click();

		await Assert.That(cut.FindAll(".sc-tab")).IsEmpty();
	}

	[TUnit.Core.Test]
	public async Task ClosingATabDoesNotAlsoActivateIt()
	{
		var cut = await RenderWithOneOpenTabAsync();

		// The close control sits inside the tab, whose own click switches to it. If the click
		// reaches both, closing the last tab leaves the editor trying to activate a tab that
		// is no longer there.
		cut.Find(".sc-tab-close").Click();

		await Assert.That(cut.Markup).Contains("TermNoAttributeSelected");
	}

	[TUnit.Core.Test]
	public async Task AnUnsavedTabAnnouncesThatStateRatherThanHidingIt()
	{
		var cut = await RenderWithOneOpenTabAsync();

		// Monaco is stubbed, so its buffer reads back empty — which differs from the stored value
		// and is exactly what makes the tab dirty. Raising the editor's change event is the only
		// route to that state; IsDirty is derived, never set directly.
		var editor = cut.FindComponent<StandaloneCodeEditor>();
		await cut.InvokeAsync(() => editor.Instance.OnDidChangeModelContent.InvokeAsync(new ModelContentChangedEvent()));

		var dirty = cut.Find(".sc-tab-dirty");

		// The dot is the only thing marking a tab as unsaved, so aria-hidden would erase that state
		// for assistive tech entirely.
		await Assert.That(dirty.GetAttribute("aria-hidden")).IsNull();
		await Assert.That(dirty.GetAttribute("role")).IsEqualTo("img");
		await Assert.That(dirty.GetAttribute("aria-label")).IsEqualTo("TermUnsaved");
	}

	/// <summary>Disposes the HttpClient this fixture owns; the handler goes with it.</summary>
	[After(Test)]
	public void DisposeApiClient() => _api.Dispose();
}
