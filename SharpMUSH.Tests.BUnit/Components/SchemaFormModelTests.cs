using System.Text.Json;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The schema form's state without rendering it: seeding, page moves, value coercion, the advisory
/// required check and how a failed action's errors are bound.
/// </summary>
public class SchemaFormModelTests
{
	private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

	private static SchemaElement Field(string key, string type = "text", bool required = false, JsonElement? fallback = null) =>
		new(Kind: "field", Key: key, Label: key.ToUpperInvariant(), Type: type, Default: fallback,
			Validation: required ? new SchemaValidation(Required: true, null, null, null, null) : null);

	private static PortalSchemaDocument Form(params SchemaPage[] pages) => new(
		Kind: "form",
		SchemaVersion: 1,
		Title: "Form",
		DataSource: null,
		Pages: pages,
		Actions: new Dictionary<string, SchemaAction>
		{
			["submit"] = new("http", "POST", "/http/form/submit", "fields", null, null),
			["routeless"] = new("http", "POST", " ", "fields", null, null),
		});

	private static SchemaPage Page(string key, int order, params SchemaElement[] elements) =>
		new(key, key, order, [new SchemaSection(null, 1, null, elements)], null, null);

	private static SchemaFormModel Loaded(PortalSchemaDocument document, SchemaData? data = null)
	{
		var model = new SchemaFormModel();
		model.Load(document, data);
		return model;
	}

	[Test]
	public async Task Load_SeedsFromData_ThenDefaults_AndSkipsDisplayElements()
	{
		var document = Form(Page("main", 1,
			Field("name", fallback: Json("\"default name\"")),
			Field("age", "number", fallback: Json("21")),
			new SchemaElement(Kind: "markdown", Key: "note", Value: "hello")));
		var data = new SchemaData(new Dictionary<string, SchemaFieldValue> { ["name"] = new(Json("\"Ada\"")) });

		var model = Loaded(document, data);

		await Assert.That(model.GetString("name")).IsEqualTo("Ada");
		await Assert.That(model.GetNumber("age")).IsEqualTo(21d);
		await Assert.That(model.Values.ContainsKey("note")).IsFalse();
	}

	[Test]
	public async Task Load_ReplacementDocument_KeepsWhatWasEntered_AndStartsAtItsFirstPage()
	{
		var model = Loaded(Form(Page("one", 1, Field("name")), Page("two", 2, Field("email"))));
		model.SetString("name", "Ada");
		model.NextPage();

		model.Load(Form(Page("next", 1, Field("name", fallback: Json("\"ignored\"")), Field("bio", fallback: Json("\"tbd\"")))), null);

		await Assert.That(model.PageIndex).IsEqualTo(0);
		await Assert.That(model.GetString("name")).IsEqualTo("Ada");
		await Assert.That(model.GetString("bio")).IsEqualTo("tbd");
	}

	[Test]
	public async Task Pages_FollowTheirOrder_AndStopAtEitherEnd()
	{
		var model = Loaded(Form(Page("second", 2), Page("first", 1)));

		await Assert.That(model.CurrentPage.Key).IsEqualTo("first");
		await Assert.That(model.IsFirstPage).IsTrue();

		model.PrevPage();
		await Assert.That(model.PageIndex).IsEqualTo(0);

		model.NextPage();
		model.NextPage();
		await Assert.That(model.CurrentPage.Key).IsEqualTo("second");
		await Assert.That(model.IsLastPage).IsTrue();
	}

	[Test]
	public async Task Values_CoerceOnRead()
	{
		var model = Loaded(Form(Page("main", 1, Field("n", "number"), Field("b", "boolean"), Field("m", "multiselect"), Field("d", "date"))));

		model.SetString("n", "not a number");
		await Assert.That(model.GetNumber("n")).IsNull();
		model.SetNumber("n", 4.5);
		await Assert.That(model.GetString("n")).IsEqualTo(4.5.ToString());

		model.SetString("b", "true");
		await Assert.That(model.GetBool("b")).IsFalse();
		model.SetBool("b", true);
		await Assert.That(model.GetBool("b")).IsTrue();

		await Assert.That(model.GetMulti("m")).IsEmpty();
		model.SetMulti("m", new HashSet<string> { "x" });
		await Assert.That(model.GetMulti("m")).IsEquivalentTo(["x"]);

		model.SetDate("d", new DateTime(2026, 3, 9));
		await Assert.That(model.GetString("d")).IsEqualTo("2026-03-09");
		await Assert.That(model.GetDate("d")).IsEqualTo(new DateTime(2026, 3, 9));
		model.SetDate("d", null);
		await Assert.That(model.GetDate("d")).IsNull();
	}

	[Test]
	public async Task ValidateRequired_FlagsEmptyRequiredFields_AndEditingClearsTheFieldError()
	{
		var model = Loaded(Form(Page("one", 1, Field("name", required: true)), Page("two", 2, Field("email", required: true), Field("bio"))));
		model.SetString("email", "  ");

		var valid = model.ValidateRequired(field => $"need {field.Label}", "fill them in");

		await Assert.That(valid).IsFalse();
		await Assert.That(model.Error("name")).IsEqualTo("need NAME");
		await Assert.That(model.Error("email")).IsEqualTo("need EMAIL");
		await Assert.That(model.Error("bio")).IsNull();
		await Assert.That(model.GlobalError).IsEqualTo("fill them in");

		model.SetString("name", "Ada");
		await Assert.That(model.Error("name")).IsNull();

		model.SetString("email", "ada@example.com");
		await Assert.That(model.ValidateRequired(_ => "x", "y")).IsTrue();
		await Assert.That(model.GlobalError).IsNull();
	}

	[Test]
	public async Task BindErrors_KeyedToFields_GlobalToTheForm()
	{
		var model = Loaded(Form(Page("main", 1, Field("name"))));

		model.BindErrors(new Dictionary<string, string> { ["name"] = "taken", ["_global"] = "nope" }, bindToFields: true, "failed");

		await Assert.That(model.Error("name")).IsEqualTo("taken");
		await Assert.That(model.GlobalError).IsEqualTo("nope");

		model.ClearErrors();
		await Assert.That(model.Error("name")).IsNull();
		await Assert.That(model.GlobalError).IsNull();
	}

	[Test]
	public async Task BindErrors_NotBoundToFields_GoToTheForm_AndNoneAtAllUsesTheFallback()
	{
		var model = Loaded(Form(Page("main", 1, Field("name"))));

		model.BindErrors(new Dictionary<string, string> { ["name"] = "taken" }, bindToFields: false, "failed");
		await Assert.That(model.Error("name")).IsNull();
		await Assert.That(model.GlobalError).IsEqualTo("taken");

		model.BindErrors(null, bindToFields: true, "failed");
		await Assert.That(model.GlobalError).IsEqualTo("failed");
	}

	[Test]
	public async Task MergeFields_TakesTheAnsweredValues()
	{
		var model = Loaded(Form(Page("main", 1, Field("name"), Field("level", "number"))));

		model.MergeFields(new Dictionary<string, JsonElement> { ["name"] = Json("\"Ada\""), ["level"] = Json("3") });

		await Assert.That(model.GetString("name")).IsEqualTo("Ada");
		await Assert.That(model.GetNumber("level")).IsEqualTo(3d);
	}

	[Test]
	public async Task Action_NeedsARoute()
	{
		var model = Loaded(Form(Page("main", 1)));

		await Assert.That(model.Action("submit")?.Route).IsEqualTo("/http/form/submit");
		await Assert.That(model.Action("routeless")).IsNull();
		await Assert.That(model.HasAction("routeless")).IsTrue();
		await Assert.That(model.Action("missing")).IsNull();
	}

	[Test]
	[Arguments(1, 1, 12)]
	[Arguments(2, 1, 6)]
	[Arguments(3, 2, 8)]
	[Arguments(2, 5, 12)]
	[Arguments(0, 0, 12)]
	[Arguments(24, 1, 1)]
	public async Task ColumnWidth_IsClampedTwelfths(int columns, int span, int expected)
		=> await Assert.That(SchemaFormModel.ColumnWidth(columns, span)).IsEqualTo(expected);
}
