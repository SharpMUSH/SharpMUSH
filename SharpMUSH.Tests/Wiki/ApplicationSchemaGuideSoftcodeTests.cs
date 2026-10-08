using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Resources;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// The seeded Help:Application Schema Guide teaches readers to type its softcode into the game. These
/// tests take that softcode out of the page, enter it the way the guide says to (each command joined into
/// one line), put it on an HTTP handler carrying the bundled verb routers, and send it the requests the
/// portal would. The JSON samples must read as the portal's own models.
/// </summary>
public partial class ApplicationSchemaGuideSoftcodeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[GeneratedRegex(@"```(?<lang>\w+)\n(?<body>.*?)\n```", RegexOptions.Singleline)]
	private static partial Regex Fence();

	[GeneratedRegex(@"\n[ \t]*")]
	private static partial Regex LineBreak();

	private static IEnumerable<string> Blocks(string language) =>
		Fence().Matches(SeededWikiPages.ApplicationSchemaGuide)
			.Where(m => m.Groups["lang"].Value == language)
			.Select(m => m.Groups["body"].Value.ReplaceLineEndings("\n"));

	/// <summary>
	/// The guide's commands as a reader enters them: a line opening with <c>&amp;</c> starts a command, and
	/// every other line continues the one before with its line break and indentation deleted.
	/// </summary>
	private static IEnumerable<string> Commands() =>
		Blocks("mushcode").SelectMany(block =>
			Regex.Split(block, @"\n(?=&)").Select(command => LineBreak().Replace(command, string.Empty)));

	private static string ExamplesRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, "examples", "packages");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			dir = dir.Parent!;
		}

		throw new DirectoryNotFoundException("Could not locate examples/packages above the test directory.");
	}

	/// <summary>The http-handler package's verb router for <paramref name="verb"/>, a one-line block scalar.</summary>
	private static async Task<string> Router(string verb)
	{
		var lines = (await File.ReadAllTextAsync(Path.Combine(ExamplesRoot(), "http-handler", "package.yaml")))
			.ReplaceLineEndings("\n").Split('\n');
		var index = Array.FindIndex(lines, line => line.TrimStart().StartsWith(verb + ": |-", StringComparison.Ordinal));
		return lines[index + 1].Trim();
	}

	/// <summary>A handler object with the verb routers and every command the guide has the reader type.</summary>
	private async Task<DBRef> GuideHandlerAsync()
	{
		var handler = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GuideHandler");
		foreach (var verb in new[] { "GET", "POST" })
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{verb} {handler}={await Router(verb)}"));
		}

		foreach (var command in Commands())
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command.Replace(" #8=", $" {handler}=")));
		}

		return handler;
	}

	private async Task<HttpHandlerResult> SendAsync(DBRef handler, string method, string path, string body = "")
	{
		var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();
		Task<Found<HttpHandlerResult>> request;
		using (TestOptionsOverride.Scope(o => o with
		{
			Database = o.Database with { HttpHandler = (uint)handler.Number, HttpRequestsPerSecond = 30 }
		}))
		{
			request = dispatcher.DispatchAsync(method, path, body, [("Content-Type", "application/json")]).AsTask();
		}

		var result = (await request.WaitAsync(TimeSpan.FromSeconds(10))).Expect<HttpHandlerResult>();
		await Assert.That(result.Status).IsEqualTo(200).Because(result.Body);
		return result;
	}

	[Test]
	public async Task GuideSoftcode_AllCommandsAreFound()
	{
		var commands = Commands().ToList();

		await Assert.That(commands.Select(c => c[..c.IndexOf(' ')]))
			.IsEquivalentTo(["&FN`FIELD", "&FN`CHOICE", "&GET`CHARGEN`SCHEMA", "&POST`CHARGEN`SUBMIT"]);
		await Assert.That(commands.All(c => !c.Contains('\n'))).IsTrue();
	}

	[Test]
	public async Task GuideSchemaRoute_AnswersTheFormThePortalDraws()
	{
		var handler = await GuideHandlerAsync();

		var result = await SendAsync(handler, "GET", "/chargen/schema");

		await Assert.That(result.ContentType).IsEqualTo("application/json");
		var document = JsonSerializer.Deserialize<PortalSchemaDocument>(result.Body, SchemaJson.Options)!;
		await Assert.That(document.IsForm).IsTrue();
		await Assert.That(document.Title).IsEqualTo("Character Application");

		var elements = document.Pages!.Single().Sections!.Single().Elements!;
		await Assert.That(elements.Select(e => $"{e.Key}|{e.Label}|{e.Type}"))
			.IsEquivalentTo([
				"charname|Character Name|text",
				"concept|Concept|text",
				"class|Class|select",
				"background|Background|textarea"]);
		await Assert.That(elements.Single(e => e.Key == "class").Options!.Select(o => o.Label))
			.IsEquivalentTo(["Fighter", "Wizard", "Rogue"]);
		await Assert.That(document.Actions!["submit"].Route).IsEqualTo("/http/chargen/submit");
	}

	[Test]
	public async Task GuideSubmitRoute_AcceptsANamedCharacter()
	{
		var handler = await GuideHandlerAsync();

		var result = await SendAsync(handler, "POST", "/chargen/submit", """{"charname":"Ada Lovelace","class":"wizard"}""");

		var answer = JsonSerializer.Deserialize<SchemaActionResult>(result.Body, SchemaJson.Options)!;
		await Assert.That(answer.Ok).IsTrue();
		await Assert.That(answer.Message).IsEqualTo("Thank you! Staff will read your application soon.");
	}

	[Test]
	[Arguments("""{"concept":"Sky pirate"}""")]
	[Arguments("""{"charname":"   "}""")]
	[Arguments("""{"charname":""}""")]
	[Arguments("{}")]
	public async Task GuideSubmitRoute_RefusesAMissingName(string body)
	{
		var handler = await GuideHandlerAsync();

		var result = await SendAsync(handler, "POST", "/chargen/submit", body);

		var answer = JsonSerializer.Deserialize<SchemaActionResult>(result.Body, SchemaJson.Options)!;
		await Assert.That(answer.Ok).IsFalse();
		await Assert.That(answer.Errors!["charname"]).IsEqualTo("Please give your character a name.");
	}

	[Test]
	public async Task GuideJsonSamples_ReadAsThePortalsModels()
	{
		var samples = Blocks("json").ToList();
		await Assert.That(samples.Count).IsEqualTo(3);

		var form = JsonSerializer.Deserialize<PortalSchemaDocument>(samples[0], SchemaJson.Options)!;
		await Assert.That(form.IsForm).IsTrue();
		await Assert.That(form.Actions!["submit"].Route).IsEqualTo("/http/chargen/submit");

		var data = JsonSerializer.Deserialize<SchemaData>(samples[1], SchemaJson.Options)!;
		await Assert.That(data.Fields!["name"].Visible).IsTrue();
		await Assert.That(data.Fields["notes"].Visible).IsFalse();

		var view = JsonSerializer.Deserialize<PortalSchemaDocument>(samples[2], SchemaJson.Options)!;
		await Assert.That(view.IsForm).IsFalse();
		await Assert.That(view.Pages!.Single().Sections!.Single().Elements!.Select(e => e.Key ?? string.Empty))
			.IsEquivalentTo(["name", "concept", "notes"]);
	}
}
