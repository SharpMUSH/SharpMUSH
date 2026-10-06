using System.Collections.Concurrent;
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
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Serves #8 and its attributes, and answers <c>api/commands/eval</c> with the expression it was sent,
/// recording each request.
/// </summary>
internal sealed class ConsoleApiHandler : HttpMessageHandler
{
	private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

	public ConcurrentQueue<PortalEvalRequest> Evaluations { get; } = new();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		object? body;
		switch (request.RequestUri!.AbsolutePath)
		{
			case "/api/commands/eval":
				var evaluation = JsonSerializer.Deserialize<PortalEvalRequest>(
					await request.Content!.ReadAsStringAsync(ct), CamelCase)!;
				Evaluations.Enqueue(evaluation);
				body = new PortalCommandResponse(["told you"], $"value of {evaluation.Expression}", false);
				break;
			case "/api/objects/8/attributes":
				body = new List<AttributeDto> { new("FN`GREET", "Hello, %0.", []) };
				break;
			case "/api/objects/8":
				body = new ObjectSummaryDto("#8", "Widget", "THING", "Wizard(#1)", []);
				break;
			case "/data/mush-defs.json":
				body = new { };
				break;
			default:
				body = null;
				break;
		}

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

		return response;
	}
}

/// <summary>
/// The Softcode Editor's console (#1578): the play button runs the buffer as it stands, as the object, with
/// the console's arguments; the input line evaluates as the character; each run lands in the open tab's
/// scrollback.
/// </summary>
public class SoftcodeEditorConsoleTests : BunitContext
{
	private readonly ConsoleApiHandler _handler = new();
	private readonly HttpClient _api;

	public SoftcodeEditorConsoleTests()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(true);
		terminal.SendCommandAsync(Arg.Any<string>()).Returns(call =>
			Task.FromResult<string[]>(call.Arg<string>().Contains("hasflag")
				? ["0"]
				: ["SHARP_OBJ:8:THING:Widget"]));

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
		// Monaco is stubbed; this is what its buffer holds, typed but not saved.
		JSInterop.Setup<string>(invocation => invocation.Identifier.EndsWith("getValue", StringComparison.Ordinal))
			.SetResult("Hi, %0 from [name(me)].");
	}

	private IRenderedComponent<Components.MudHarness> RenderWithFunctionOpen()
	{
		var cut = Render<Components.MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.SoftcodeEditor>());

		cut.WaitForElement(".ob-item", TimeSpan.FromSeconds(5)).Click();
		cut.WaitForElement(".sc-tree-toggle", TimeSpan.FromSeconds(5)).Click();
		cut.FindAll(".sc-attr-item").Single(e => e.TextContent.Contains("GREET")).Click();
		cut.WaitForElement(".sc-tab", TimeSpan.FromSeconds(5));
		return cut;
	}

	[TUnit.Core.Test]
	public async Task RunningTheBuffer_SendsItUnsaved_AsTheObject_WithTheArguments()
	{
		var cut = RenderWithFunctionOpen();

		cut.Find(".sc-console-toggle").Click();
		cut.Find(".sc-console-addarg").Click();
		cut.Find(".sc-console-arg-input").Input("Bob");
		cut.Find(".sc-eval").Click();

		await cut.WaitForAssertionAsync(
			async () => await Assert.That(cut.FindAll(".sc-console-entry")).Count().IsEqualTo(1),
			TimeSpan.FromSeconds(5));

		var sent = _handler.Evaluations.Single();
		await Assert.That(sent.Expression).IsEqualTo("Hi, %0 from [name(me)].");
		await Assert.That(sent.Object).IsEqualTo(8);
		await Assert.That(sent.Arguments!).IsEquivalentTo(["Bob"]);

		var entry = cut.Find(".sc-console-entry");
		await Assert.That(entry.QuerySelector(".sc-console-code")!.TextContent).IsEqualTo("#8/FN`GREET");
		// The buffer differs from what #8 holds, and the entry says the run was of unsaved text.
		await Assert.That(entry.QuerySelector(".sc-console-unsaved")).IsNotNull();
		await Assert.That(entry.QuerySelector(".sc-console-argline")!.TextContent).IsEqualTo("%0=Bob");
		await Assert.That(entry.QuerySelector(".sc-console-out")!.TextContent).IsEqualTo("told you");
		await Assert.That(entry.QuerySelector(".sc-console-result")!.TextContent).IsEqualTo("value of Hi, %0 from [name(me)].");
		// Who the code ran as is stated, not left to be discovered.
		await Assert.That(cut.Find(".sc-console-hint").TextContent).IsEqualTo("TermConsoleSemantics(Widget(#8))");
	}

	[TUnit.Core.Test]
	public async Task ATypedExpression_RunsAsTheCharacter_AndIsKeptForUp()
	{
		var cut = RenderWithFunctionOpen();
		cut.Find(".sc-console-toggle").Click();

		var line = cut.Find(".sc-console-line");
		line.Input("add(1,2)");
		line.KeyDown("Enter");

		await cut.WaitForAssertionAsync(
			async () => await Assert.That(cut.FindAll(".sc-console-entry")).Count().IsEqualTo(1),
			TimeSpan.FromSeconds(5));

		var sent = _handler.Evaluations.Single();
		await Assert.That(sent.Expression).IsEqualTo("add(1,2)");
		await Assert.That(sent.Object).IsNull();
		await Assert.That(sent.Arguments).IsNull();
		await Assert.That(cut.Find(".sc-console-line").GetAttribute("value")).IsEqualTo(string.Empty);

		cut.Find(".sc-console-line").KeyDown("ArrowUp");
		await cut.WaitForAssertionAsync(
			async () => await Assert.That(cut.Find(".sc-console-line").GetAttribute("value")).IsEqualTo("add(1,2)"),
			TimeSpan.FromSeconds(5));
	}

	[TUnit.Core.Test]
	public async Task TheScrollbackBelongsToTheTabThatRan()
	{
		var cut = RenderWithFunctionOpen();
		cut.Find(".sc-console-toggle").Click();
		cut.Find(".sc-eval").Click();
		await cut.WaitForAssertionAsync(
			async () => await Assert.That(cut.FindAll(".sc-console-entry")).Count().IsEqualTo(1),
			TimeSpan.FromSeconds(5));

		// With the tab closed the console has no tab's scrollback to show.
		cut.Find(".sc-tab-close").Click();
		await cut.WaitForAssertionAsync(
			async () => await Assert.That(cut.FindAll(".sc-console-entry")).IsEmpty(),
			TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".sc-console-hint").TextContent).IsEqualTo("TermConsoleAsYou");
	}

	/// <summary>Disposes the HttpClient this fixture owns; the handler goes with it.</summary>
	[After(Test)]
	public void DisposeApiClient() => _api.Dispose();
}
