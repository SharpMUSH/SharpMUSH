using System.Net;
using System.Text;
using System.Text.Json;
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
/// Serves <c>#12</c> with one attribute, XP, whose keys are whatever the last PUT to its keys endpoint stored, and
/// <c>#13</c> with one attribute of its own. A PUT waits for <see cref="PutGate"/> when one is set.
/// </summary>
internal sealed class KeysApiHandler : HttpMessageHandler
{
	private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

	public string? ReadKey { get; set; }
	public string? WriteKey { get; private set; } = "=#7";
	public List<(string Path, string Key)> Puts { get; } = [];
	public TaskCompletionSource? PutGate { get; set; }
	public int LedgerReads { get; private set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		if (request.Method == HttpMethod.Put && path.StartsWith("/api/objects/12/attributes/XP/keys/", StringComparison.Ordinal))
		{
			if (PutGate is { } gate) await gate.Task.WaitAsync(ct);
			var key = JsonSerializer.Deserialize<SetAttributeKeyRequest>(await request.Content!.ReadAsStringAsync(ct), CamelCase)!.Key;
			Puts.Add((path, key));
			var stored = key.Length == 0 ? null : key;
			if (path.EndsWith("/read", StringComparison.Ordinal)) ReadKey = stored;
			else WriteKey = stored;
			return new HttpResponseMessage(HttpStatusCode.NoContent);
		}

		if (path == "/api/objects/12/attributes") LedgerReads++;
		object? body = path switch
		{
			"/api/objects/12/attributes" => new[] { new AttributeDto("XP", "10", []) { ReadKey = ReadKey, WriteKey = WriteKey } },
			"/api/objects/12" => new ObjectSummaryDto("#12", "Ledger", "THING", "God(#1)", []),
			"/api/objects/13/attributes" => new[] { new AttributeDto("NOTES", "hi", []) },
			"/api/objects/13" => new ObjectSummaryDto("#13", "Journal", "THING", "God(#1)", []),
			"/data/mush-defs.json" => new { },
			_ => null,
		};

		return body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(JsonSerializer.Serialize(body, CamelCase), Encoding.UTF8, "application/json")
			};
	}
}

/// <summary>
/// The Softcode Editor's keys panel: it shows the open attribute's read and write keys, and Apply sends only the
/// ones that changed, then shows what the server stored.
/// </summary>
public class SoftcodeEditorKeysTests : BunitContext
{
	private readonly HttpClient _api;
	private readonly KeysApiHandler _handler = new();

	public SoftcodeEditorKeysTests()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(true);
		terminal.SendCommandAsync(Arg.Any<string>()).Returns(call =>
			Task.FromResult<string[]>(call.Arg<string>().Contains("hasflag") ? ["0"] : ["SHARP_OBJ:12:THING:Ledger", "SHARP_OBJ:13:THING:Journal"]));

		_api = new HttpClient(_handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(_api);

		Services
			.AddMudServices()
			.AddSingleton(terminal)
			.AddSingleton(factory)
			.AddSingleton<ObjectApiService>()
			.AddSingleton<GameCommandService>()
			.AddSingleton(sp => new HelpService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")))
			.AddSingleton(sp => new MushQueryService(
				sp.GetRequiredService<ITerminalService>(), NullLogger<MushQueryService>.Instance))
			.AddEchoLocalizer();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private async Task<IRenderedComponent<Components.MudHarness>> RenderWithTheKeysOpenAsync()
	{
		var cut = Render<Components.MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.SoftcodeEditor>());

		await cut.WaitForElement(".ob-item", TimeSpan.FromSeconds(5)).ClickAsync();
		await cut.WaitForElement(".sc-attr-item", TimeSpan.FromSeconds(5)).ClickAsync();
		cut.WaitForElement(".sc-tab", TimeSpan.FromSeconds(5));
		await cut.Find(".sc-keys-toggle").ClickAsync();
		cut.WaitForElement(".sc-keys", TimeSpan.FromSeconds(5));
		return cut;
	}

	[TUnit.Core.Test]
	public async Task ThePanelShowsTheStoredKeys()
	{
		var cut = await RenderWithTheKeysOpenAsync();

		await Assert.That(cut.Find("input[name=read-key]").GetAttribute("value")).IsEqualTo(string.Empty);
		await Assert.That(cut.Find("input[name=write-key]").GetAttribute("value")).IsEqualTo("=#7");
		await Assert.That(cut.Find("input[name=read-key]").GetAttribute("placeholder")).IsEqualTo("role^builder");
	}

	[TUnit.Core.Test]
	public async Task ApplySendsOnlyTheKeyThatChanged()
	{
		var cut = await RenderWithTheKeysOpenAsync();

		await cut.Find("input[name=read-key]").InputAsync(new() { Value = "role^clerk" });
		await cut.Find(".sc-keys-apply").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".sc-save-feedback"), TimeSpan.FromSeconds(5));
		await Assert.That(_handler.Puts).IsEquivalentTo([("/api/objects/12/attributes/XP/keys/read", "role^clerk")]);
		await Assert.That(cut.Find("input[name=read-key]").GetAttribute("value")).IsEqualTo("role^clerk");
		await Assert.That(cut.Find(".sc-save-feedback").TextContent).Contains("TermKeysSaved");
	}

	[TUnit.Core.Test]
	public async Task ClearingAFieldClearsThatKey()
	{
		var cut = await RenderWithTheKeysOpenAsync();

		await cut.Find("input[name=write-key]").InputAsync(new() { Value = "" });
		await cut.Find(".sc-keys-apply").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".sc-save-feedback"), TimeSpan.FromSeconds(5));
		await Assert.That(_handler.Puts).IsEquivalentTo([("/api/objects/12/attributes/XP/keys/write", "")]);
		await Assert.That(_handler.WriteKey).IsNull();
	}

	[TUnit.Core.Test]
	public async Task ARefreshKeepsAnUnsavedEditToTheOtherKey()
	{
		var cut = await RenderWithTheKeysOpenAsync();
		await cut.Find("input[name=write-key]").InputAsync(new() { Value = "role^draft" });

		_handler.ReadKey = "=#9";
		await cut.Find("button[aria-label=TermRefreshAttributes]").ClickAsync();

		cut.WaitForState(() => cut.Find("input[name=read-key]").GetAttribute("value") == "=#9", TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("input[name=write-key]").GetAttribute("value")).IsEqualTo("role^draft");
	}

	[TUnit.Core.Test]
	public async Task ASaveFinishedAfterSelectingAnotherObjectRefreshesTheSavingTab()
	{
		var cut = await RenderWithTheKeysOpenAsync();
		await cut.Find("input[name=read-key]").InputAsync(new() { Value = "role^clerk" });

		_handler.PutGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		// Not awaited: the click's handler waits on the gate.
		var applying = cut.Find(".sc-keys-apply").ClickAsync();
		await cut.FindAll(".ob-item")[1].ClickAsync();
		cut.WaitForState(() => cut.FindAll(".ob-item--on").Any(item => item.TextContent.Contains("Journal")), TimeSpan.FromSeconds(5));
		var readsBefore = _handler.LedgerReads;

		_handler.PutGate.SetResult();

		await applying;
		cut.WaitForState(() => cut.FindAll(".sc-save-feedback").Count > 0, TimeSpan.FromSeconds(5));
		await Assert.That(_handler.LedgerReads).IsGreaterThan(readsBefore);
		await Assert.That(cut.Find(".sc-keys-apply").HasAttribute("disabled")).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task ApplyWaitsForAChange()
	{
		var cut = await RenderWithTheKeysOpenAsync();

		await Assert.That(cut.Find(".sc-keys-apply").HasAttribute("disabled")).IsTrue();
	}

	[TUnit.Core.After(TUnit.Core.HookType.Test)]
	public void DisposeApiClient() => _api.Dispose();
}
