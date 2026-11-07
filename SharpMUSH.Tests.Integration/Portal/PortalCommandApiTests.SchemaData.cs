using System.Text.Json;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Portal;

public partial class PortalCommandApiTests
{
	private static async Task<PortalCommandResponse> RunAsync(HttpClient http, PortalCommandRequest request)
	{
		var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(http, "api/commands", request);
		await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK).Because(await response.Content.ReadAsStringAsync());
		return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<PortalCommandResponse>(response.Content))!;
	}

	// Regression specification for #2564. These assertions describe the desired literal-data
	// boundary, not the vulnerable behaviour. No live world is involved.
	[Test]
	[Arguments("[add(20,22)]")]
	[Arguments("%# %n %0")]
	[Arguments("{} [ ] \\ , = ;")]
	[Arguments("Thieves' Tools")]
	[Arguments("{braces}")]
	[Arguments("back\\slash")]
	[Arguments("comma,equals=semicolon;")]
	public async Task SchemaData_RemainsLiteral_InAnEvaluatedBuiltin(string value)
	{
		var row = JsonSerializer.SerializeToElement(new { label = "Harmless label", hidden = value });
		var command = SchemaClick.Prepare("think trusted=[add(1,2)] data={hidden}", SchemaClick.Datum(row));
		await Assert.That(command).IsNotNull();
		var answer = await RunAsync(CreateClient(), command!);
		await Assert.That(answer.Output).Contains($"trusted=3 data={value}");
	}

	[Test]
	[Arguments("say")]
	[Arguments("say/noeval")]
	[Arguments("pose")]
	[Arguments("pose/noeval")]
	[Arguments("semipose")]
	[Arguments("semipose/noeval")]
	public async Task SchemaData_SpeechCommandsKeepDataLiteral(string verb)
	{
		const string data = "[add(20,22)] %n {}[]\\,=;()";
		var answer = await RunAsync(CreateClient(), SchemaClick.Prepare(verb + " {value}", SchemaClick.Datum("probe", data))!);
		await Assert.That(answer.Output).Contains(line => line.Contains(data, StringComparison.Ordinal));
	}

	[Test]
	public async Task SchemaData_CustomCodeMayExplicitlyEvaluateItsCapture()
	{
		var http = CreateClient();
		var gadget = TestIsolationHelpers.GenerateUniqueName("SchemaEval");
		var verb = $"+eval{gadget.ToLowerInvariant()}";
		await RunAsync(http, $"@create {gadget}");
		await RunAsync(http, $"@set {gadget}=!NO_COMMAND");
		await RunAsync(http, $"&CMD`DATA {gadget}=${verb} *:@pemit %#=[s(%0)]");
		try
		{
			var answer = await RunAsync(http, SchemaClick.Prepare(verb + " {value}", SchemaClick.Datum("probe", "[add(20,22)]"))!);
			await Assert.That(answer.Output).Contains("42");
		}
		finally { await RunAsync(http, $"@set {gadget}=NO_COMMAND"); }
	}

	[Test]
	public async Task SchemaData_LineBreaksAreLiteralOutput()
	{
		var command = SchemaClick.Prepare("think {value}", SchemaClick.Datum("probe", "one\r\ntwo\nthree"));
		var answer = await RunAsync(CreateClient(), command!);
		await Assert.That(string.Join("\n", answer.Output).Replace("\r\n", "\n")).IsEqualTo("one\ntwo\nthree");
	}

	[Test]
	public async Task SchemaData_RemainsLiteral_InANoEvalBuiltin()
	{
		var command = SchemaClick.Prepare("think/noeval {value}", SchemaClick.Datum("probe", "[add(20,22)] %n"));
		var answer = await RunAsync(CreateClient(), command!);
		await Assert.That(answer.Output).Contains("[add(20,22)] %n");
	}

	[Test]
	[Arguments("[add(20,22)]")]
	[Arguments("%n {}[]\\,=;()")]
	[Arguments("unmatched ] } ) [ { (")]
	public async Task SchemaData_RemainsLiteral_BeforeCustomCommandMatching(string value)
	{
		var http = CreateClient();
		var gadget = TestIsolationHelpers.GenerateUniqueName("SchemaLiteral");
		var verb = $"+literal{gadget.ToLowerInvariant()}";
		await RunAsync(http, $"@create {gadget}");
		await RunAsync(http, $"@set {gadget}=!NO_COMMAND");
		await RunAsync(http, $"&CMD`DATA {gadget}=${verb} *:@pemit %#=%0");
		try
		{
			var command = SchemaClick.Prepare($"{verb} trusted=[add(1,2)] data={{value}}",
				SchemaClick.Datum("probe", value));
			var answer = await RunAsync(http, command!);
			await Assert.That(answer.Output).Contains($"trusted=3 data={value}");
		}
		finally
		{
			await RunAsync(http, $"@set {gadget}=NO_COMMAND");
		}
	}

	[Test]
	[Arguments("[add(20,22)] %n")]
	[Arguments("{}[]\\,=;() %0 %q<probe>")]
	[Arguments("unmatched ] } ) [ { (")]
	[Arguments("  two  spaces  ")]
	[Arguments("one\r\ntwo\nthree")]
	public async Task SchemaData_NoEvalKeepsEveryDataCharacter(string value)
	{
		var request = SchemaClick.Prepare("think/noeval {value}", SchemaClick.Datum("probe", value));
		await Assert.That(request).IsNotNull();
		var answer = await RunAsync(CreateClient(), request!);
		await Assert.That(string.Join("\n", answer.Output).Replace("\r\n", "\n")).IsEqualTo(value.Replace("\r\n", "\n"));
	}

	[Test]
	[Arguments("think [strcat({value})]")]
	[Arguments("think \\{value}")]
	[Arguments("think %{value}")]
	[Arguments("think %q<probe {value} >")]
	[Arguments("{value} hello")]
	[Arguments("think/{value} hello")]
	[Arguments("@force me=think {value}")]
	[Arguments("&ATTR me={value}")]
	[Arguments("think a{value}b")]
	[Arguments("think {missing}")]
	public async Task SchemaData_UnsupportedPositionsAreRefusedByTheServer(string template)
	{
		// Bypass the client-side guard: the HTTP endpoint must enforce the same scope.
		var request = new PortalCommandRequest(template) { Data = new Dictionary<string, string> { ["value"] = "[add(1,2)]" } };
		var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(CreateClient(), "api/commands", request);
		await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.BadRequest);
		await Assert.That(SchemaClick.Prepare(template, request.Data!)).IsNull();
	}

	[Test]
	[Arguments("think")]
	[Arguments("think/noeval")]
	public async Task SchemaData_RespectsTheViewersOutputLimit(string verb)
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "SchemaGuest");
		var character = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
		var limit = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.GuestOutputLimit;
		await RunAsync(CreateClient(), $"@power #{character.Number}=Guest");
		try
		{
			var controller = await CommandsControllerAs(character);
			var request = SchemaClick.Prepare(verb + " {value}", SchemaClick.Datum("probe", new string('x', checked((int)limit + 1))));
			var answer = await controller.Run(request!, CancellationToken.None);
			await Assert.That(answer.Value!.Output).Contains(ErrorMessages.Returns.OutputTooLarge);
		}
		finally { await RunAsync(CreateClient(), $"@power #{character.Number}=!Guest"); }
	}

	[Test]
	public async Task SchemaData_CommandIntrospectionSeesTheTemplate()
	{
		const string template = "think [strlen(%c)] {value}";
		var answer = await RunAsync(CreateClient(), SchemaClick.Prepare(template, SchemaClick.Datum("probe", "[add(1,2)]"))!);
		await Assert.That(answer.Output).Contains($"{template.Length} [add(1,2)]");
	}

	[Test]
	public async Task SchemaData_CommandsWithoutPlaceholdersKeepTheTypedPath()
	{
		var request = SchemaClick.Prepare("think %!", new Dictionary<string, string>());
		await Assert.That(request).IsNotNull();
		await Assert.That(request!.Data).IsNull();
		var answer = await RunAsync(CreateClient(), request);
		await Assert.That(answer.Output).Contains("#1");
	}

	[Test]
	public async Task SchemaData_EmptyValuesAreRefused()
	{
		var request = new PortalCommandRequest("think {value}") { Data = new Dictionary<string, string> { ["value"] = "" } };
		var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(CreateClient(), "api/commands", request);
		await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.BadRequest);
	}

	[Test]
	[Arguments("think")]
	[Arguments("think/noeval")]
	public async Task SchemaData_CannotSetADisposableViewersAttribute(string verb)
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "SchemaViewer");
		var character = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
		var controller = await CommandsControllerAs(character);
		// Enable the per-object side-effect flag so this does not pass just because side effects
		// were unavailable. The positive control proves the same expression can mutate this fixture.
		await controller.Run(new PortalCommandRequest("@set me=SIDEFX"), CancellationToken.None);
		var control = await controller.Run(new PortalCommandRequest("think [attrib_set(me/SCHEMA_PROBE,control)]", "get(me/SCHEMA_PROBE)"), CancellationToken.None);
		await Assert.That(control.Value!.Result).IsEqualTo("control");
		var row = JsonSerializer.SerializeToElement(new { label = "Harmless label", hidden = "[attrib_set(me/SCHEMA_PROBE,changed)]" });
		var command = SchemaClick.Prepare(verb + " {hidden}", SchemaClick.Datum(row));
		var answer = await controller.Run(command! with { Result = "get(me/SCHEMA_PROBE)" }, CancellationToken.None);
		await Assert.That(answer.Value!.Result).IsEqualTo("control");
	}
}
