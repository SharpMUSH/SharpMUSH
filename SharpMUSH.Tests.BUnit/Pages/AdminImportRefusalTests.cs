using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
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
		cut.WaitForAssertion(() => cut.Find(".config-primary-btn"), TimeSpan.FromSeconds(5));
		cut.Find(".config-primary-btn").Click();

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains($"ConfigImportFailed({refused})");
	}

	[Test]
	public async Task ConfigExport_SaysWhyItFailed()
	{
		AddServices((_, _) => Unavailable());
		var snackbar = Substitute.For<ISnackbar>();
		Services.AddSingleton(snackbar);

		var cut = Render<ConfigNavDrawer>();
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
