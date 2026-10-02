using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Pages.Admin.Config;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Answers each request from its method and the path it asked for.</summary>
file sealed class RouteHandler(Func<HttpMethod, string, HttpResponseMessage> respond) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(respond(request.Method, request.RequestUri!.AbsolutePath.TrimStart('/')));
}

/// <summary>
/// The configuration and database-import pages when the server refuses or does not answer.
/// </summary>
/// <remarks>
/// Their services used to answer <see langword="null"/>, <see langword="false"/> or throw, and the pages
/// logged it and carried on: the configuration home read "0 settings" in every group, a refused config
/// import stopped its spinner and said nothing, a refused database upload left the form as it was, a
/// conversion the server had dropped polled forever over a blank card, and a refused cancel reset the
/// page as though the conversion had stopped.
/// </remarks>
public class AdminImportRefusalTests : TrackingBunitContext
{
	private const string Reason = "The configuration store is offline.";

	private void AddServices(Func<HttpMethod, string, HttpResponseMessage> respond)
	{
		var client = Track(new HttpClient(new RouteHandler(respond))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<AdminConfigService>()
			.AddSingleton<ConfigSchemaService>()
			.AddSingleton<DatabaseConversionService>()
			.AddEchoLocalizer();

		// The configuration home gates its admin-tools aside with AuthorizeView.
		AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
		new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	private static HttpResponseMessage Refusal(HttpStatusCode status, string reason) =>
		Json(status, JsonSerializer.Serialize(reason));

	private static HttpResponseMessage Unavailable() =>
		Json(HttpStatusCode.ServiceUnavailable, $$"""{"error":"{{Reason}}"}""");

	private static HttpResponseMessage Started() =>
		Json(HttpStatusCode.OK, """{"sessionId":"s1","message":"Conversion started"}""");

	private static HttpResponseMessage Halfway() =>
		Json(HttpStatusCode.OK, JsonSerializer.Serialize(new ConversionProgress
		{
			TotalObjects = 10, ProcessedObjects = 5, CurrentPhase = "Objects", PercentageComplete = 50
		}));

	private static void Click(IRenderedComponent<ImportDatabase> cut, string label) =>
		cut.FindAll("button").First(b => b.TextContent.Contains(label)).Click();

	private IRenderedComponent<ImportDatabase> StartConversion()
	{
		var cut = Render<ImportDatabase>();
		cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("+V-1\n~0\n***END OF DUMP***\n", "outdb"));
		Click(cut, "StartConversion");
		return cut;
	}

	/// <summary>Picks the database, then the source game's mush.cnf, and starts.</summary>
	private IRenderedComponent<ImportDatabase> StartConversionWithConfig()
	{
		var cut = Render<ImportDatabase>();
		cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("+V-1\n~0\n***END OF DUMP***\n", "outdb"));
		// MudFileUpload keeps the input it used beside a fresh one, so the picker is found by what it accepts.
		cut.FindComponents<InputFile>().Last(input => input.Find("input").GetAttribute("accept") == ".cnf,.conf,.txt")
			.UploadFiles(InputFileContent.CreateFromText("mud_name Elsewhere\n", "mush.cnf"));
		cut.WaitForAssertion(() => cut.Find(".dbimport-config-name"), TimeSpan.FromSeconds(5));
		Click(cut, "StartConversion");
		return cut;
	}

	/// <summary>
	/// The source game's mush.cnf is applied before the database is sent, so the conversion reads the game's
	/// own master room, ancestors and handlers rather than SharpMUSH's defaults.
	/// </summary>
	[Test]
	public async Task DatabaseImport_AppliesTheMushCnfFirst()
	{
		var asked = new List<string>();
		AddServices((method, path) =>
		{
			asked.Add($"{method} {path}");
			return path switch
			{
				"api/configuration/import" => Json(HttpStatusCode.OK, "{}"),
				"api/databaseconversion/upload" => Started(),
				_ => Halfway()
			};
		});

		var cut = StartConversionWithConfig();

		cut.WaitForAssertion(() =>
		{
			if (!asked.Contains("POST api/databaseconversion/upload"))
				throw new InvalidOperationException("not uploaded yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(asked.IndexOf("POST api/configuration/import")).IsGreaterThanOrEqualTo(0);
		await Assert.That(asked.IndexOf("POST api/configuration/import"))
			.IsLessThan(asked.IndexOf("POST api/databaseconversion/upload"));
	}

	/// <summary>A configuration the server refuses stops the import: converting against the wrong settings is what it was given to prevent.</summary>
	[Test]
	public async Task DatabaseImport_ARefusedMushCnf_ImportsNothing()
	{
		const string refused = "Error importing configuration: unreadable file";
		var asked = new List<string>();
		AddServices((method, path) =>
		{
			asked.Add($"{method} {path}");
			return path == "api/configuration/import" ? Refusal(HttpStatusCode.BadRequest, refused) : Started();
		});

		var cut = StartConversionWithConfig();

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains($"AdmImportConfigFailed({refused})");
		await Assert.That(asked).DoesNotContain("POST api/databaseconversion/upload");
	}

	[Test]
	public async Task ConfigHome_SaysWhyItHasNoCounts()
	{
		AddServices((_, _) => Unavailable());

		var cut = Render<ConfigIndex>();
		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".mud-alert").TextContent).Contains("FailedToLoadSchema");
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains(Reason);
		await Assert.That(cut.FindAll(".config-cat-count")).IsEmpty();
	}

	[Test]
	public async Task ConfigImport_SaysWhyTheServerRefusedIt()
	{
		const string refused = "Error importing configuration: unknown directive 'mud_nmae' on line 3";
		AddServices((_, _) => Refusal(HttpStatusCode.BadRequest, refused));

		var cut = Render<ImportConfig>();
		cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("mud_nmae Test\n", "mush.cnf"));
		cut.WaitForAssertion(() => cut.Find("button.kit-capsule--primary"), TimeSpan.FromSeconds(5));
		cut.Find("button.kit-capsule--primary").Click();

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains($"ConfigImportFailed({refused})");
	}

	[Test]
	public async Task ConfigExport_SaysWhyItFailed()
	{
		AddServices((_, _) => Unavailable());
		var snackbar = Substitute.For<ISnackbar>();
		Services.AddSingleton(snackbar);

		var cut = Render<ConfigSidebar>();
		cut.FindAll("button").First(b => b.TextContent.Contains("Export")).Click();

		cut.WaitForAssertion(() => snackbar.Received().Add(
			Arg.Is<string>(m => m.Contains("ExportFailed") && m.Contains(Reason)),
			Severity.Error, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>()), TimeSpan.FromSeconds(5));
		await Assert.That(snackbar.ReceivedCalls()).IsNotEmpty();
	}

	[Test]
	public async Task DatabaseUpload_SaysWhyTheServerRefusedIt()
	{
		AddServices((_, _) => Refusal(HttpStatusCode.BadRequest, "No file uploaded"));

		var cut = StartConversion();

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains("DatabaseImportFailed(No file uploaded)");
		await Assert.That(cut.FindAll(".dbimport-dropzone")).IsNotEmpty();
	}

	[Test]
	public async Task AConversionTheServerDroppedStopsPollingAndSaysSo()
	{
		AddServices((_, path) => path.StartsWith("api/databaseconversion/progress/")
			? Refusal(HttpStatusCode.NotFound, "Session not found")
			: Started());

		var cut = StartConversion();

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains("DatabaseImportFailed(Session not found)");
		await Assert.That(cut.FindAll(".dbimport-dropzone")).IsNotEmpty();
	}

	[Test]
	public async Task ARefusedCancelLeavesTheConversionShowing()
	{
		AddServices((_, path) => path switch
		{
			_ when path.StartsWith("api/databaseconversion/progress/") => Halfway(),
			_ when path.StartsWith("api/databaseconversion/cancel/") =>
				Refusal(HttpStatusCode.NotFound, "Session not found or already completed"),
			_ => Started()
		});

		var cut = StartConversion();
		cut.WaitForAssertion(() => Click(cut, "CancelConversion"), TimeSpan.FromSeconds(5));

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent)
			.Contains("ConversionCancelFailed(Session not found or already completed)");
		await Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("CancelConversion"))).IsTrue();
	}
}
