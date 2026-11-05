using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// A click on a value sends what the viewer meant: the element's command, filled from the value and run as the viewer,
/// or its dialog of choices. A value that could add to the command is not clickable.
/// </summary>
public class SchemaClickTests : TrackingBunitContext
{
	private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
	}

	private readonly List<(string Path, string? Body)> _requests = [];

	public SchemaClickTests()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(_ => Track(new HttpClient(new Handler(request =>
		{
			var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
			lock (_requests) _requests.Add((request.RequestUri!.PathAndQuery, body));
			return request.RequestUri!.AbsolutePath switch
			{
				"/api/commands" => new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new PortalCommandResponse(["You roll Strength: 14 (roll 3f2a)."], null, false))
				},
				"/http/sheet/attack" => new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("""
						{"schema_version":1,"kind":"form","title":"Attack","pages":[{"sections":[{"elements":[
						 {"kind":"field","key":"bonus","label":"Situational bonus","type":"number"}]}]}],
						 "actions":{"submit":{"transport":"http","method":"POST","route":"/http/sheet/strike","payload":"fields"}}}
						""", System.Text.Encoding.UTF8, "application/json")
				},
				_ => new HttpResponseMessage(HttpStatusCode.NotFound),
			};
		}))
		{ BaseAddress = new("https://localhost/") }));
		Services.AddMudServices().AddSingleton(factory)
			.AddSingleton(new GameCommandService(factory))
			.AddSingleton(new SchemaAppService(factory, NullLogger<SchemaAppService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static SchemaData Data(string key, string json) =>
		new(new Dictionary<string, SchemaFieldValue> { [key] = new(JsonDocument.Parse(json).RootElement.Clone()) });

	[Test]
	public async Task A_template_fills_from_the_value_and_refuses_what_could_add_to_the_command()
	{
		var datum = new Dictionary<string, string> { ["key"] = "Strength", ["label"] = "Str", ["bad"] = "x;@pemit me=hi", ["empty"] = "" };

		await Assert.That(SchemaClick.Fill("+check {key}", datum)).IsEqualTo("+check Strength");
		await Assert.That(SchemaClick.Fill("+check {bad}", datum)).IsNull();
		await Assert.That(SchemaClick.Fill("+check {missing}", datum)).IsNull();
		await Assert.That(SchemaClick.Fill("+check {empty}", datum)).IsNull();
		await Assert.That(SchemaClick.Fill("+check [add(1,2)] {key}", datum)).IsEqualTo("+check [add(1,2)] Strength");
	}

	[Test]
	public async Task A_dialog_address_carries_the_key_and_stays_on_this_server()
	{
		await Assert.That(SchemaClick.DialogUrl("/http/sheet/attack", "Long sword")).IsEqualTo("http/sheet/attack?key=Long%20sword");
		await Assert.That(SchemaClick.DialogUrl("http/sheet/attack?mine=1", "Axe")).IsEqualTo("http/sheet/attack?mine=1&key=Axe");
		await Assert.That(SchemaClick.DialogUrl("https://elsewhere.example/attack", "Axe")).IsNull();
		await Assert.That(SchemaClick.DialogUrl("/http/sheet/attack", null)).IsNull();
	}

	[Test]
	public async Task A_clicked_stat_tile_runs_its_command_as_typed_and_shows_what_it_printed()
	{
		var changed = 0;
		var cut = Render<SchemaDisplay>(p => p
			.Add(x => x.Element, new SchemaElement(Kind: "stats", RowsField: "abilities", Command: "+check {key}"))
			.Add(x => x.Data, Data("abilities", """[{"key":"Strength","label":"STR","value":"14"},{"key":"x;y","label":"Odd","value":"1"}]"""))
			.Add(x => x.OnChanged, () => changed++));

		var tiles = cut.FindAll(".schema-stat");
		await Assert.That(tiles[0].GetAttribute("role")).IsEqualTo("button");
		await Assert.That(tiles[1].GetAttribute("role")).IsNull();

		await tiles[0].ClickAsync(new());
		cut.WaitForState(() => cut.FindAll(".schema-click-output").Any(o => o.TextContent.Contains("14 (roll 3f2a)")));

		var command = _requests.Single(r => r.Path == "/api/commands");
		await Assert.That(JsonNode.Parse(command.Body!)!["command"]!.GetValue<string>()).IsEqualTo("+check Strength");
		await Assert.That(changed).IsEqualTo(1);
	}

	[Test]
	public async Task A_keyvalue_row_a_chip_and_a_table_row_click_with_their_own_values()
	{
		var data = new SchemaData(new Dictionary<string, SchemaFieldValue>
		{
			["Perception"] = new(JsonSerializer.SerializeToElement("+7")),
			["skills"] = new(JsonSerializer.SerializeToElement(new[] { "Athletics", "Stealth" })),
			["gear"] = new(JsonDocument.Parse("""[{"id":"rope","name":"Rope"}]""").RootElement.Clone()),
		});

		var kv = Render<SchemaDisplay>(p => p.Add(x => x.Element, new SchemaElement(Kind: "keyvalue", Fields: ["Perception"], Command: "+check {key}")).Add(x => x.Data, data));
		await kv.Find("button.schema-click").ClickAsync(new());
		var chips = Render<SchemaDisplay>(p => p.Add(x => x.Element, new SchemaElement(Kind: "chips", SrcField: "skills", Command: "+check {text}")).Add(x => x.Data, data));
		await chips.FindAll(".schema-chip--click")[1].ClickAsync(new());
		var table = Render<SchemaDisplay>(p => p.Add(x => x.Element, new SchemaElement(Kind: "table", RowsField: "gear",
			Columns: [new SchemaColumn("name", "Item")], Command: "+use {id}")).Add(x => x.Data, data));
		await table.Find("td button.schema-click").ClickAsync(new());

		table.WaitForState(() => { lock (_requests) return _requests.Count(r => r.Path == "/api/commands") == 3; });
		var sent = _requests.Where(r => r.Path == "/api/commands").Select(r => JsonNode.Parse(r.Body!)!["command"]!.GetValue<string>());
		await Assert.That(sent).IsEquivalentTo(["+check Perception", "+check Stealth", "+use rope"]);
	}

	[Test]
	public async Task A_button_with_a_command_and_no_action_runs_the_command()
	{
		var cut = Render<SchemaDisplay>(p => p
			.Add(x => x.Element, new SchemaElement(Kind: "button", Key: "initiative", Label: "Roll initiative", Command: "+init {key}")));

		await cut.Find("button").ClickAsync(new());

		cut.WaitForState(() => cut.FindAll(".schema-click-output").Count > 0);
		await Assert.That(JsonNode.Parse(_requests.Single(r => r.Path == "/api/commands").Body!)!["command"]!.GetValue<string>()).IsEqualTo("+init initiative");
	}

	[Test]
	public async Task A_dialog_value_opens_the_form_its_route_answers_for_that_value()
	{
		var cut = Render<DialogHarness>(p => p.AddChildContent<SchemaDisplay>(d => d
			.Add(x => x.Element, new SchemaElement(Kind: "table", RowsField: "weapons", Columns: [new SchemaColumn("name", "Weapon")], Dialog: "/http/sheet/attack"))
			.Add(x => x.Data, Data("weapons", """[{"key":"longsword","name":"Longsword"}]"""))));

		// The click waits on the dialog, so it is started, not awaited.
		await cut.StartClickAsync(cut.Find("td button.schema-click"));

		cut.WaitForState(() => cut.FindAll(".mud-dialog").Any(d => d.TextContent.Contains("Situational bonus")));
		await Assert.That(_requests.Select(r => r.Path)).Contains("/http/sheet/attack?key=longsword");
		await Assert.That(_requests.Select(r => r.Path)).DoesNotContain("/api/commands");
	}

	[Test]
	public async Task The_checker_names_a_click_that_does_nothing_or_leaves_the_server()
	{
		var problems = PortalSchema.Check(JsonNode.Parse("""
			{"kind":"view","pages":[{"sections":[{"elements":[
			 {"kind":"markdown","value":"hi","command":"+check {key}"},
			 {"kind":"chips","src_field":"tags","dialog":"https://elsewhere.example/roll"},
			 {"kind":"button","label":"Roll","command":"+roll"},
			 {"kind":"button","label":"Nothing"}]}]}]}
			"""));

		await Assert.That(problems).IsEquivalentTo([
			"pages[0].sections[0].elements[0]: a markdown has no values to click, so command does nothing",
			"pages[0].sections[0].elements[1]: dialog route \"https://elsewhere.example/roll\" is not on this server",
			"pages[0].sections[0].elements[3]: a button needs an action, a command or a dialog",
		]);
	}
}
