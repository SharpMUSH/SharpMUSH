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
/// Serves the two <c>api/objects</c> reads the editor needs to open a tab, for the holder <c>#acct:5</c> (number int.MinValue + 5).
/// </summary>
file sealed class AccountApiHandler(ObjectSummaryDto summary, IReadOnlyList<AttributeDto> attributes) : HttpMessageHandler
{
	private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		object? body = path switch
		{
			"/api/objects/-2147483643/attributes" => attributes,
			"/api/objects/-2147483643" => summary,
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
/// The Softcode Editor on an account's holder: the object browser lists it as <c>#acct:5</c>, a tab names its
/// attributes that way, and Run and the console say the buffer runs as the character, since a holder runs nothing.
/// </summary>
public class SoftcodeEditorAccountTabTests : BunitContext
{
	private readonly HttpClient _api;

	public SoftcodeEditorAccountTabTests()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(true);
		terminal.SendCommandAsync(Arg.Any<string>()).Returns(call =>
			Task.FromResult<string[]>(call.Arg<string>().Contains("hasflag")
				? ["1"]
				: ["SHARP_ACCT:5"]));

		var handler = new AccountApiHandler(
			new ObjectSummaryDto("#acct:5", "Account 5", "ACCOUNT", "God(#1)", []),
			[new AttributeDto("XP", "10", [])]);
		_api = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(_api);

		Services
			.AddMudServices()
			.AddSingleton(terminal)
			.AddSingleton(factory)
			.AddSingleton<ObjectApiService>()
			.AddSingleton<GameCommandService>()
			.AddSingleton(sp => new HelpService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")))
			.AddSingleton(sp => new MushQueryService(sp.GetRequiredService<ITerminalService>()))
			.AddEchoLocalizer();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private async Task<IRenderedComponent<Components.MudHarness>> RenderWithTheAccountOpenAsync()
	{
		var cut = Render<Components.MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.SoftcodeEditor>());

		await cut.WaitForElement(".ob-item", TimeSpan.FromSeconds(5)).ClickAsync();
		await cut.WaitForElement(".sc-attr-item", TimeSpan.FromSeconds(5)).ClickAsync();
		cut.WaitForElement(".sc-tab", TimeSpan.FromSeconds(5));
		return cut;
	}

	[TUnit.Core.Test]
	public async Task TheBrowserListsTheAccountByItsReference()
	{
		var cut = Render<Components.MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.SoftcodeEditor>());

		var item = cut.WaitForElement(".ob-item", TimeSpan.FromSeconds(5));

		await Assert.That(item.TextContent).Contains("#acct:5");
	}

	[TUnit.Core.Test]
	public async Task ATabNamesTheAttributeOnTheAccount()
	{
		var cut = await RenderWithTheAccountOpenAsync();

		await Assert.That(cut.Find(".sc-tab-label").TextContent.Trim()).IsEqualTo("#acct:5/XP");
	}

	[TUnit.Core.Test]
	public async Task RunSaysTheBufferRunsAsTheCharacter()
	{
		var cut = await RenderWithTheAccountOpenAsync();

		await Assert.That(cut.Find(".sc-eval").GetAttribute("aria-label")).IsEqualTo("TermEvalTooltipAsYou");
		await cut.Find(".sc-console-toggle").ClickAsync();
		await Assert.That(cut.Find(".sc-console").TextContent).Contains("TermConsoleAsYou");
	}

	[TUnit.Core.After(TUnit.Core.HookType.Test)]
	public void DisposeApiClient() => _api.Dispose();
}
