using System.Text.Json;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

public class SchemaApplicationStateTests
{
	private static JsonElement J(string value) => JsonSerializer.SerializeToElement(value);
	private static PortalSchemaDocument Screen(string kind, params SchemaElement[] fields) => new(kind, 1, kind, null,
		[new("main", null, 1, [new(null, 1, null, fields)], null, null)], null);
	private static SchemaAction Action(bool reset = false, bool merge = false) => new("http", "POST", "/http/test", "fields", new(null, null, merge, reset), null);

	[Test]
	public async Task UnsupportedSchemaNeverReplacesTheCurrentPresentation()
	{
		var state = new SchemaApplicationState();
		state.Load(Screen("form"), null);
		var token = state.Begin();
		state.Apply(token, Action(), new(true, null, null, Screen("script"), "/apps/elsewhere", "ignored"), "failed");
		await Assert.That(state.Document!.Kind).IsEqualTo("form");
		await Assert.That(state.Form.GlobalError).Contains("Unsupported application schema");
		await Assert.That(state.Redirect).IsNull();
		state.Load(Screen("view") with { SchemaVersion = 99 }, null);
		await Assert.That(state.Document).IsNull();
		await Assert.That(state.Form.GlobalError).Contains("Unsupported application schema");
	}

	[Test]
	public async Task NamedResetRemovesOnlySelectedKeysThenSeedsResultingDefaults()
	{
		SchemaAction? action = null;
		try { action = JsonSerializer.Deserialize<SchemaAction>("""{"route":"/http/test","on_success":{"reset_fields":["title"]}}""", SchemaJson.Options); }
		catch (JsonException) { }
		await Assert.That(action is not null).IsTrue();
		var state = new SchemaApplicationState();
		state.Load(Screen("form", new SchemaElement(Key: "title"), new SchemaElement(Key: "retained")), null);
		state.Form.SetString("title", "old"); state.Form.SetString("retained", "keep");
		var token = state.Begin();
		state.Apply(token, action!, new(true, null, null, Screen("form", new SchemaElement(Key: "title", Default: J("default")), new SchemaElement(Key: "retained", Default: J("ignored"))), null, null), "failed");
		await Assert.That(state.Form.GetString("title")).IsEqualTo("default");
		await Assert.That(state.Form.GetString("retained")).IsEqualTo("keep");
	}

	[Test]
	public async Task Success_ResetMergeThenReplacementDataAndDefaults()
	{
		var state = new SchemaApplicationState();
		state.Load(Screen("form", new SchemaElement(Key: "title")), null);
		state.Form.SetString("title", "draft");
		var token = state.Begin();
		var result = new SchemaActionResult(true, null, new Dictionary<string, JsonElement> { ["title"] = J("merged") },
			Screen("view", new SchemaElement(Key: "title"), new SchemaElement(Key: "new", Default: J("default"))), null, null,
			new(new Dictionary<string, SchemaFieldValue> { ["title"] = new(J("authoritative")) }));
		await Assert.That(state.Apply(token, Action(true, true), result, "failed")).IsTrue();
		await Assert.That(state.Document!.Kind).IsEqualTo("view");
		await Assert.That(state.Form.GetString("title")).IsEqualTo("authoritative");
		await Assert.That(state.Form.GetString("new")).IsEqualTo("default");
	}

	[Test]
	public async Task Failure_ReplacesPresentationButNeverResetsMergesOrRedirects()
	{
		var state = new SchemaApplicationState();
		state.Load(Screen("view"), null);
		state.Form.SetString("title", "draft");
		var token = state.Begin();
		var result = new SchemaActionResult(false, new Dictionary<string, string> { ["removed"] = "bucket closed" },
			new Dictionary<string, JsonElement> { ["title"] = J("must not merge") }, Screen("form", new SchemaElement(Key: "title")), "/apps/elsewhere", "must not toast");
		await Assert.That(state.Apply(token, Action(true, true), result, "failed")).IsTrue();
		await Assert.That(state.Form.GetString("title")).IsEqualTo("draft");
		await Assert.That(state.Document!.Kind).IsEqualTo("form");
		await Assert.That(state.Form.GlobalError).IsEqualTo("bucket closed");
		await Assert.That(state.Redirect).IsNull();
	}

	[Test]
	public async Task RetiredResponsesAndConcurrentAdmissionsCannotChangeCurrentInstance()
	{
		var state = new SchemaApplicationState();
		state.Load(Screen("form"), null);
		var retired = state.Begin();
		await Assert.That(state.Begin()).IsEqualTo(Guid.Empty);
		state.Load(Screen("view"), null);
		var current = state.Begin();
		await Assert.That(state.Apply(retired, Action(), new(true, null, null, Screen("form"), null, null), "failed")).IsFalse();
		await Assert.That(state.Busy).IsTrue();
		await Assert.That(state.Apply(current, Action(), new(true, null, null, null, "/apps/jobs", null), "failed")).IsTrue();
		await Assert.That(state.Redirect).IsEqualTo("/apps/jobs");
		await Assert.That(state.Apply(current, Action(), new(true, null, null, Screen("form"), null, null), "failed")).IsFalse();
	}
}
