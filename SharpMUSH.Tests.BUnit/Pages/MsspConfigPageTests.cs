using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin.Config;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Answers <c>api/mssp</c> with a fixed report, and keeps what a save sends.</summary>
internal sealed class MsspApi : HttpMessageHandler
{
	public Dictionary<string, string[]>? Saved { get; private set; }

	public static readonly MsspSettingsResponse Response = new(
		[
			new MsspReportedVariable("NAME", ["Test Game"]),
			new MsspReportedVariable("PLAYERS", ["3"]),
			new MsspReportedVariable("PORT", ["4201"]),
			new MsspReportedVariable("GENRE", ["Fantasy"]),
			new MsspReportedVariable("MY THING", ["kept"])
		],
		new Dictionary<string, string[]> { ["GENRE"] = ["Fantasy"], ["MY THING"] = ["kept"] });

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Method == HttpMethod.Put)
		{
			Saved = (await request.Content!.ReadFromJsonAsync<MsspSettingsRequest>(cancellationToken))!.Settings;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(Response with { Settings = Saved })
			};
		}

		return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Response) };
	}
}

/// <summary>
/// The MSSP page: a variable the server reports is a greyed, read-only row saying where it comes
/// from; the rest are edited and saved as the <c>mssp</c> option.
/// </summary>
public class MsspConfigPageTests : TrackingBunitContext
{
	private readonly MsspApi _api = new();

	public MsspConfigPageTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<MsspService>()
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<MsspConfig> RenderLoaded()
	{
		var cut = Render<MsspConfig>();
		cut.WaitForAssertion(() => cut.Find("[data-mssp='NAME']"));
		return cut;
	}

	[Test]
	public async Task AVariableTheServerReportsIsReadOnlyAndSaysWhereItIsSet()
	{
		var cut = RenderLoaded();
		var name = cut.Find("[data-mssp='NAME']");

		await Assert.That(name.ClassList.Contains("mssp-row--server")).IsTrue();
		var input = name.QuerySelector("input")!;
		await Assert.That(input.HasAttribute("disabled")).IsTrue();
		await Assert.That(input.GetAttribute("value")).IsEqualTo("Test Game");
		await Assert.That(name.QuerySelector(".mssp-source code")!.TextContent).IsEqualTo("mud_name");
		await Assert.That(name.QuerySelector("a.cfg-icon-btn")!.GetAttribute("href")).IsEqualTo("/admin/config/net");

		await Assert.That(cut.Find("[data-mssp='PLAYERS'] .mssp-source").TextContent).Contains("Counted by the server");
	}

	/// <summary>SSL is left out of the report while ssl_port is 0, and the row says so rather than showing a blank.</summary>
	[Test]
	public async Task AServerVariableLeftOutOfTheReportReadsNotReported()
	{
		var cut = RenderLoaded();

		await Assert.That(cut.Find("[data-mssp='SSL'] input").GetAttribute("value")).IsEqualTo("Not reported");
	}

	[Test]
	public async Task TheSavedSettingsFillTheirRows()
	{
		var cut = RenderLoaded();

		var fantasy = cut.FindAll("[data-mssp='GENRE'] .cfg-pill").Single(pill => pill.TextContent.Trim() == "Fantasy");
		await Assert.That(fantasy.GetAttribute("aria-pressed")).IsEqualTo("true");
		await Assert.That(cut.Find(".mssp-other-name").GetAttribute("value")).IsEqualTo("MY THING");
		await Assert.That(cut.FindAll(".cfg-unsaved").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AnEditShowsTheUnsavedBarAndSavingSendsEverySetting()
	{
		var cut = RenderLoaded();

		cut.FindAll("[data-mssp='STATUS'] .cfg-pill").Single(pill => pill.TextContent.Trim() == "Open Beta").Click();
		cut.Find("[data-mssp='CONTACT'] input").Change("staff@example.com");

		await Assert.That(cut.Find(".cfg-count").TextContent).IsEqualTo("2 changes");

		cut.FindAll(".cfg-unsaved .kit-capsule").Last().Click();
		cut.WaitForState(() => _api.Saved is not null);

		await Assert.That(JsonSerializer.Serialize(_api.Saved)).IsEqualTo(JsonSerializer.Serialize(new Dictionary<string, string[]>
		{
			["CONTACT"] = ["staff@example.com"],
			["GENRE"] = ["Fantasy"],
			["STATUS"] = ["Open Beta"],
			["MY THING"] = ["kept"]
		}));
	}

	[Test]
	public async Task AValueThatCannotBeSavedIsMarkedAndNotSent()
	{
		var cut = RenderLoaded();

		cut.Find("[data-mssp='CONTACT'] input").Change("two\tfields");
		cut.FindAll(".cfg-unsaved .kit-capsule").Last().Click();

		await Assert.That(cut.Find("[data-mssp='CONTACT'] .cfg-row-error").TextContent).Contains("control characters");
		await Assert.That(_api.Saved).IsNull();
	}
}
