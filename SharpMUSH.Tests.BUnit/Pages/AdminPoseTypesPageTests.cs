using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin.Scenes;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Admin &gt; Scenes &gt; Pose types: each type with a sample drawn as the story draws it, the attributes that did not
/// read, and changes sent as <c>+scene/type/*</c> commands whose refusal the page shows.
/// </summary>
public class AdminPoseTypesPageTests : TrackingBunitContext
{
	private readonly CatalogueApi _api = new();

	public AdminPoseTypesPageTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new GameCommandService(sp.GetRequiredService<IHttpClientFactory>()))
			.AddLocalization();
		AddAuthorization().SetAuthorized("staff").SetPolicies("layout.admin");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<MudHarness> RenderPage()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<AdminPoseTypes>());
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".adm-pose-type").Count == 0) throw new InvalidOperationException("types not rendered yet");
		}, TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task ListsEachType_WithASampleInItsPresentation()
	{
		using var culture = CultureScope.For("en");
		var cut = RenderPage();

		await Assert.That(cut.FindAll(".adm-pose-type").Count).IsEqualTo(3);
		var samples = cut.FindAll(".adm-pose-sample .story-row");
		await Assert.That(samples.Select(r => r.GetAttribute("data-pose-type"))).IsEquivalentTo(new[] { "ic", "ooc", "radio" });
		await Assert.That(samples[0].ClassList).Contains("story-row--prose");
		await Assert.That(samples[1].ClassList).Contains("story-row--band");
		await Assert.That(samples[2].ClassList).Contains("story-row--message");
		await Assert.That(samples[2].GetAttribute("style")).IsEqualTo("--pose-tone: var(--info);");
		await Assert.That(cut.Find(".adm-pose-sample .story-bubble-from").TextContent).IsEqualTo("Wren on 104.5")
			.Because("a message sample carries a frequency, so its header shows");
	}

	[Test]
	public async Task ListsTheAttributesThatDidNotRead()
	{
		var cut = RenderPage();

		var problem = cut.Find(".adm-pose-problem");
		await Assert.That(problem.GetAttribute("data-pose-type")).IsEqualTo("telepathy");
		await Assert.That(problem.TextContent).Contains("TYPE`TELEPATHY");
		await Assert.That(problem.TextContent).Contains("presentation must be one of");
	}

	[Test]
	public async Task ARefusedAdd_ShowsWhatTheGameSaid()
	{
		var cut = RenderPage();

		await cut.Find(".adm-pose-new-key input").InputAsync("radio");
		await cut.Find(".adm-pose-new-label input").InputAsync("Radio again");
		await cut.Find("button.adm-pose-add-submit").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".adm-pose-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".adm-pose-error").TextContent).Contains(CatalogueApi.Exists);
		await Assert.That(_api.Commands).IsEquivalentTo(new[] { "+scene/type/add radio=Radio again" });
	}

	[Test]
	public async Task SavingAChangedField_SendsItsCommand()
	{
		var cut = RenderPage();

		var card = cut.FindAll(".adm-pose-type")[2];
		await card.QuerySelector(".adm-pose-label input")!.InputAsync("Comms");
		await cut.FindAll(".adm-pose-type")[2].QuerySelector("button.adm-pose-save")!.ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (_api.Commands.Count == 0) throw new InvalidOperationException("not sent yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(_api.Commands).IsEquivalentTo(new[] { "+scene/type/set radio/label=Comms" });
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".adm-pose-type")[2].QuerySelector(".kit-card-title")!.TextContent != "Comms")
				throw new InvalidOperationException("not read back yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".adm-pose-error").Count).IsEqualTo(0);
	}

	/// <summary>Serves api/scenes/types and the +scene/type commands the way the scene package answers them.</summary>
	private sealed class CatalogueApi : HttpMessageHandler
	{
		public const string Exists = "SCENE: There is already a pose type called radio.";

		public List<string> Commands { get; } = [];

		private string _radioLabel = "Radio";

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path == "/api/commands")
			{
				var sent = (await request.Content!.ReadFromJsonAsync<PortalCommandRequest>(cancellationToken))!;
				lock (Commands) Commands.Add(sent.Command);
				IReadOnlyList<string> output = [];
				string? result = null;
				if (sent.Command.StartsWith("+scene/type/add radio=", StringComparison.Ordinal))
				{
					output = [Exists];
					result = "Radio";
				}
				else if (sent.Command.StartsWith("+scene/type/set radio/label=", StringComparison.Ordinal))
				{
					_radioLabel = sent.Command["+scene/type/set radio/label=".Length..];
					result = _radioLabel;
				}
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PortalCommandResponse(output, result, false)) };
			}

			if (path != "/api/scenes/types") return new HttpResponseMessage(HttpStatusCode.NotFound);
			var body = $$"""
			{"types":[{"key":"ic","label":"In character","presentation":"prose","tone":"","icon":"","hidden":false,"order":10},
			          {"key":"ooc","label":"OOC","presentation":"band","tone":"muted","icon":"","hidden":false,"order":20},
			          {"key":"radio","label":"{{_radioLabel}}","presentation":"message","tone":"info","icon":"radio","hidden":false,"order":30}],
			 "problems":[{"key":"telepathy","reason":"presentation must be one of prose, band, message, aside, notice"}],
			 "hidden":[]}
			""";
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		}
	}
}
