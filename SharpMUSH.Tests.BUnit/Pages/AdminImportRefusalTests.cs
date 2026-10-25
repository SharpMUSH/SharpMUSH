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

/// <summary>
/// Answers each request from its method and the path it asked for, keeping each body in <paramref name="bodies"/>
/// when given one.
/// </summary>
file sealed class RouteHandler(Func<HttpMethod, string, HttpResponseMessage> respond, List<string>? bodies = null)
	: HttpMessageHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (bodies is not null && request.Content is not null)
		{
			bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
		}

		return respond(request.Method, request.RequestUri!.AbsolutePath.TrimStart('/'));
	}
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

	private void AddServices(Func<HttpMethod, string, HttpResponseMessage> respond, List<string>? bodies = null, IReadOnlyList<ImportSessionStatus>? sessions = null,
		bool previousRetained = false)
	{
		var client = Track(new HttpClient(new RouteHandler((method, path) => (method.Method, path) switch
		{
			(_, "api/databaseconversion/sessions") => Json(HttpStatusCode.OK, JsonSerializer.Serialize(sessions ?? [])),
			("GET", "api/databaseconversion/previous") => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new PreviousWorldStatus(previousRetained))),
			_ => respond(method, path)
		}, bodies))
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

	private static Task Click(IRenderedComponent<ImportDatabase> cut, string label) =>
		cut.FindAll("button").First(b => b.TextContent.Contains(label)).ClickAsync();

	private async Task<IRenderedComponent<ImportDatabase>> StartConversion()
	{
		var cut = Render<ImportDatabase>();
		cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("+V-1\n~0\n***END OF DUMP***\n", "outdb"));
		await Click(cut, "StartConversion");
		return cut;
	}

	/// <summary>Picks the database, then the source game's mush.cnf, and starts.</summary>
	private async Task<IRenderedComponent<ImportDatabase>> StartConversionWithConfig()
	{
		var cut = Render<ImportDatabase>();
		cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("+V-1\n~0\n***END OF DUMP***\n", "outdb"));
		// MudFileUpload keeps the input it used beside a fresh one, so the picker is found by what it accepts.
		cut.FindComponents<InputFile>().Last(input => input.Find("input").GetAttribute("accept") == ".cnf,.conf,.txt")
			.UploadFiles(InputFileContent.CreateFromText("mud_name Elsewhere\n", "mush.cnf"));
		cut.WaitForAssertion(() => cut.Find(".dbimport-config-name"), TimeSpan.FromSeconds(5));
		await Click(cut, "StartConversion");
		return cut;
	}

	/// <summary>
	/// The source game's mush.cnf goes up with the database, in the one request, so the server applies it only
	/// once the upload is in and can put the game's own configuration back if the conversion does not finish.
	/// </summary>
	[Test]
	public async Task DatabaseImport_SendsTheMushCnfWithTheDatabase()
	{
		var asked = new List<string>();
		var bodies = new List<string>();
		AddServices((method, path) =>
		{
			asked.Add($"{method} {path}");
			return path == "api/databaseconversion/upload" ? Started() : Halfway();
		}, bodies);

		var cut = await StartConversionWithConfig();

		cut.WaitForAssertion(() =>
		{
			if (!asked.Contains("POST api/databaseconversion/upload"))
				throw new InvalidOperationException("not uploaded yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(asked).DoesNotContain("POST api/configuration/import")
			.Because("a configuration applied on its own would stay if the upload then failed");
		await Assert.That(bodies.Single(b => b.Contains("name=file"))).Contains("name=configFile")
			.And.Contains("mud_name Elsewhere");
	}

	/// <summary>
	/// A refused upload, the configuration's fault or the database's, says why and does not claim the mush.cnf
	/// was applied: the server changes nothing before the conversion starts.
	/// </summary>
	[Test]
	public async Task DatabaseImport_ARefusedMushCnf_IsNotReportedAsApplied()
	{
		const string refused = "Error importing configuration: unreadable file";
		AddServices((_, path) => path == "api/databaseconversion/upload"
			? Refusal(HttpStatusCode.BadRequest, refused)
			: Halfway());

		var cut = await StartConversionWithConfig();

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains($"DatabaseImportFailed({refused})");
		await Assert.That(cut.Markup).DoesNotContain("AdmImportConfigApplied");
	}

	/// <summary>
	/// A conversion that failed, or stopped with no errors (a database with no objects), applied nothing, so it
	/// does not say it succeeded or that the mush.cnf was applied.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DatabaseImport_AFailedConversion_DoesNotReportTheMushCnfApplied(bool stoppedWithoutErrors)
	{
		var result = stoppedWithoutErrors
			? new ConversionResult { Aborted = true }
			: new ConversionResult { Errors = ["Not a PennMUSH database."] };
		AddServices((_, path) => path switch
		{
			"api/databaseconversion/upload" => Started(),
			"api/databaseconversion/progress/s1" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new ConversionProgress
			{
				TotalObjects = 10, ProcessedObjects = 10, CurrentPhase = "Done", PercentageComplete = 100
			})),
			_ => Json(HttpStatusCode.OK, JsonSerializer.Serialize(result))
		});

		var cut = await StartConversionWithConfig();

		// The progress view has a grid too, so wait for the failed result itself.
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("ConversionErrors")) throw new InvalidOperationException("no result yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).DoesNotContain("AdmImportConfigApplied");
	}

	/// <summary>
	/// Softcode the import rewrote is listed on its own, every attribute, apart from the warnings, so an
	/// administrator can go through the list and test each one.
	/// </summary>
	[Test]
	public async Task DatabaseImport_ListsTheRewrittenSoftcode()
	{
		var rewritten = new SoftcodeRewrite("#9/DESCRIBE", "%c/%x colour written as ansi()", "%crRed%cn", "ansi(r,Red)");
		var byHand = new SoftcodeRewrite("#10/AWAY", "%c/%x colour written as ansi(); check by hand: colour-code-in-register",
			"[setq(0,%cr)]%q0", "[setq(0,)]%q0");
		AddRewriteServices(rewritten, byHand);

		var cut = await StartConversion();

		cut.WaitForAssertion(() => cut.Find(".dbimport-rewrite"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".dbimport-rewrite-note").Select(line => line.TextContent))
			.IsEquivalentTo([rewritten.ToString(), byHand.ToString()]);
		await Assert.That(cut.FindAll(".dbimport-rewrite-code").Select(code => code.TextContent))
			.IsEquivalentTo(["%crRed%cn", "ansi(r,Red)", "[setq(0,%cr)]%q0", "[setq(0,)]%q0"]);
		await Assert.That(cut.Markup).Contains("AdmImportRewrites(2)").And.Contains("AdmImportRewritesLead")
			.And.Contains("AdmImportRewriteBefore").And.Contains("AdmImportRewriteAfter");
	}

	/// <summary>The whole report downloads as a text file, the rewritten softcode before and after included.</summary>
	[Test]
	public async Task DatabaseImport_DownloadsTheImportLog()
	{
		var rewritten = new SoftcodeRewrite("#9/DESCRIBE", "%c/%x colour written as ansi()", "%crRed%cn", "ansi(r,Red)");
		AddRewriteServices(rewritten);

		var cut = await StartConversion();
		cut.WaitForAssertion(() => cut.Find(".dbimport-rewrite"), TimeSpan.FromSeconds(5));
		await cut.FindAll("button").First(button => button.TextContent.Contains("AdmImportDownloadLog")).ClickAsync();

		var save = JSInterop.Invocations.Single(call => call.Identifier == "SharpMUSH.Files.saveText");
		await Assert.That((string)save.Arguments[0]!).StartsWith("sharpmush-import-").And.EndsWith(".log");
		await Assert.That((string)save.Arguments[1]!).IsEqualTo("text/plain");
		await Assert.That((string)save.Arguments[2]!).Contains("Source: RhostMUSH")
			.And.Contains("#9/DESCRIBE: %c/%x colour written as ansi()")
			.And.Contains("    %crRed%cn").And.Contains("    ansi(r,Red)");
	}

	private void AddRewriteServices(params SoftcodeRewrite[] rewrites)
		=> AddServices((_, path) => path switch
		{
			"api/databaseconversion/upload" => Started(),
			"api/databaseconversion/progress/s1" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new ConversionProgress
			{
				TotalObjects = 10, ProcessedObjects = 10, CurrentPhase = "Done", PercentageComplete = 100
			})),
			_ => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new ConversionResult
			{
				SourceName = "RhostMUSH",
				Warnings = [$"Softcode was rewritten in {rewrites.Length} attribute(s)"],
				SoftcodeRewrites = [.. rewrites]
			}))
		});

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
		await cut.Find("button.kit-capsule--primary").ClickAsync();

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
		await cut.FindAll("button").First(b => b.TextContent.Contains("Export")).ClickAsync();

		cut.WaitForAssertion(() => snackbar.Received().Add(
			Arg.Is<string>(m => m.Contains("ExportFailed") && m.Contains(Reason)),
			Severity.Error, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>()), TimeSpan.FromSeconds(5));
		await Assert.That(snackbar.ReceivedCalls()).IsNotEmpty();
	}

	[Test]
	public async Task DatabaseUpload_SaysWhyTheServerRefusedIt()
	{
		AddServices((_, _) => Refusal(HttpStatusCode.BadRequest, "No file uploaded"));

		var cut = await StartConversion();

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

		var cut = await StartConversion();

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

		var cut = await StartConversion();
		cut.WaitForAssertion(() => cut.FindAll("button").First(b => b.TextContent.Contains("CancelConversion")), TimeSpan.FromSeconds(5));
		await Click(cut, "CancelConversion");

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mud-alert").TextContent)
			.Contains("ConversionCancelFailed(Session not found or already completed)");
		await Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("CancelConversion"))).IsTrue();
	}
	[Test]
	public async Task ValidatedPreviewWaitsForExplicitRestartConfirmationBeforePromoting()
	{
		var asked = new List<string>();
		var bodies = new List<string>();
		AddServices((method, path) =>
		{
			asked.Add($"{method} {path}");
			return path switch
			{
				"api/databaseconversion/upload" => Started(),
				"api/databaseconversion/progress/s1" => Json(HttpStatusCode.OK, "{\"percentageComplete\":100}"),
				"api/databaseconversion/result/s1" => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new ConversionResult { ImportSessionId = "s1", RequiresPromotion = true, PlayersConverted = 1 })),
				"api/databaseconversion/promote/s1" => Json(HttpStatusCode.Accepted, "{}"),
				_ => throw new InvalidOperationException("Unexpected request: " + path)
			};
		}, bodies);
		var cut = await StartConversion();
		cut.WaitForAssertion(() => cut.Find(".dbimport-promote"), TimeSpan.FromSeconds(5));
		await Assert.That(asked).DoesNotContain("POST api/databaseconversion/promote/s1");
		await Assert.That(cut.Find(".dbimport-promote").HasAttribute("disabled")).IsTrue();
		await cut.Find(".dbimport-confirm-restart input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });
		await Click(cut, "AdmImportPromote");
		cut.WaitForAssertion(() => cut.Find(".dbimport-restart-required"), TimeSpan.FromSeconds(5));
		await Assert.That(asked.Count(request => request == "POST api/databaseconversion/promote/s1")).IsEqualTo(1);
		await Assert.That(bodies.Last()).Contains("true");
	}

	[Test]
	public async Task ReloadedReadyPreviewCanBeReviewedWithoutUploadingAgain()
	{
		AddServices((_, path) => throw new InvalidOperationException("No upload or promotion expected: " + path),
			sessions: [new("saved-preview", "Ready", new ConversionResult { PlayersConverted = 2 }, null)]);
		var cut = Render<ImportDatabase>();
		cut.WaitForAssertion(() => cut.Find(".dbimport-resume"));
		await cut.Find(".dbimport-resume").ClickAsync();
		await Assert.That(cut.FindAll(".dbimport-promote").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".dbimport-promote").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task ReloadedValidationReportKeepsPollingRatherThanClaimingSuccessEarly()
	{
		AddServices((_, path) => path switch
		{
			"api/databaseconversion/progress/validating" => Json(HttpStatusCode.OK, "{\"percentageComplete\":100,\"currentPhase\":\"Validating\"}"),
			"api/databaseconversion/result/validating" => Refusal(HttpStatusCode.NotFound, "Not completed"),
			_ => throw new InvalidOperationException("Unexpected request: " + path)
		}, sessions: [new("validating", "Validating", new ConversionResult { PlayersConverted = 2 }, null)]);
		var cut = Render<ImportDatabase>();
		cut.WaitForAssertion(() => cut.Find(".dbimport-resume"));
		await cut.Find(".dbimport-resume").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".dbimport-cancel"));
		await Assert.That(cut.FindAll(".dbimport-promote")).IsEmpty();
		await Assert.That(cut.FindAll(".dbimport-dropzone")).IsEmpty();
	}

	[Test]
	public async Task RecentImportsNameTheFileTheServerAndWhereEachStands()
	{
		AddServices((_, path) => throw new InvalidOperationException("Unexpected request: " + path),
			sessions: [new("s1", "Committed", new ConversionResult { SourceName = "TinyMUX" }, null, DateTimeOffset.UnixEpoch, "unload.flat")]);
		var cut = Render<ImportDatabase>();
		cut.WaitForAssertion(() => cut.Find(".dbimport-session"));
		await Assert.That(cut.Find(".dbimport-session-file").TextContent).IsEqualTo("unload.flat");
		await Assert.That(cut.Find(".dbimport-session-format").TextContent).IsEqualTo("TinyMUX");
		await Assert.That(cut.Find(".dbimport-session-state").TextContent).IsEqualTo("AdmImportStateCommitted");
		await Assert.That(cut.Find(".dbimport-session-started").TextContent).IsNotEmpty();
	}

	[Test]
	public async Task APreviousWorldCanBeDiscardedOnceConfirmed()
	{
		var asked = new List<string>();
		AddServices((method, path) =>
		{
			asked.Add($"{method} {path}");
			return path == "api/databaseconversion/previous/discard"
				? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new PreviousWorldStatus(false)))
				: throw new InvalidOperationException("Unexpected request: " + path);
		}, previousRetained: true);
		var cut = Render<ImportDatabase>();
		cut.WaitForAssertion(() => cut.Find(".dbimport-discard-previous"));
		await Assert.That(cut.Find(".dbimport-discard-previous").HasAttribute("disabled")).IsTrue();
		await cut.Find(".dbimport-confirm-discard input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });
		await Click(cut, "AdmImportDiscardPrevious");
		cut.WaitForState(() => cut.FindAll(".dbimport-previous").Count == 0);
		await Assert.That(asked).IsEquivalentTo(["POST api/databaseconversion/previous/discard"]);
	}
}
