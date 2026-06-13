using System.Text.Json;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Verifies the Portal Schema Document (Area 21) and the action response envelope deserialize from
/// softcode-style snake_case JSON via <see cref="SchemaJson.Options"/>. This is the contract the
/// renderers depend on, and the error shape the form renderer binds.
/// </summary>
public class SchemaSerializationTests
{
	private const string FormJson = """
	{
	  "kind": "form",
	  "schema_version": 1,
	  "title": "Character Generation",
	  "data_source": "/http/chargen?objid=1",
	  "pages": [
	    { "key": "p1", "title": "Race & Class", "order": 1, "sections": [
	      { "name": "Basics", "order": 1, "visible_to": "public", "elements": [
	        { "kind": "field", "key": "strength", "label": "Strength", "type": "number",
	          "validation": { "required": true, "min": 3, "max": 18, "max_length": 2 }, "visible_to": "public" },
	        { "kind": "field", "key": "class", "label": "Class", "type": "select",
	          "options": [ {"value":"fighter","label":"Fighter"}, {"value":"wizard","label":"Wizard"} ] },
	        { "kind": "markdown", "value": "## Welcome" },
	        { "kind": "image", "src_field": "portrait", "alt": "Portrait" },
	        { "kind": "button", "label": "Roll", "action": "roll" }
	      ]}
	    ]}
	  ],
	  "actions": {
	    "submit": { "transport": "http", "method": "POST", "route": "/http/chargen/submit", "payload": "fields",
	      "on_success": { "navigate": "/character/Gandalf", "toast": "Created!", "merge_fields": true },
	      "on_error": { "bind_field_errors": true } }
	  }
	}
	""";

	[TUnit.Core.Test]
	public async Task FormDocument_DeserializesAllParts()
	{
		var doc = JsonSerializer.Deserialize<PortalSchemaDocument>(FormJson, SchemaJson.Options);

		await Assert.That(doc).IsNotNull();
		await Assert.That(doc!.IsForm).IsTrue();
		await Assert.That(doc.SchemaVersion).IsEqualTo(1);
		await Assert.That(doc.Title).IsEqualTo("Character Generation");
		await Assert.That(doc.DataSource).IsEqualTo("/http/chargen?objid=1");

		var section = doc.Pages![0].Sections![0];
		await Assert.That(section.VisibleTo).IsEqualTo("public");

		var strength = section.Elements![0];
		await Assert.That(strength.Kind).IsEqualTo("field");
		await Assert.That(strength.Type).IsEqualTo("number");
		await Assert.That(strength.Validation!.Required).IsTrue();
		await Assert.That(strength.Validation.Min).IsEqualTo(3d);
		await Assert.That(strength.Validation.Max).IsEqualTo(18d);
		await Assert.That(strength.Validation.MaxLength).IsEqualTo(2);

		var classField = section.Elements[1];
		await Assert.That(classField.Options!.Count).IsEqualTo(2);
		await Assert.That(classField.Options![0].Value).IsEqualTo("fighter");

		var image = section.Elements[3];
		await Assert.That(image.SrcField).IsEqualTo("portrait");

		var submit = doc.Actions!["submit"];
		await Assert.That(submit.Route).IsEqualTo("/http/chargen/submit");
		await Assert.That(submit.OnSuccess!.MergeFields).IsTrue();
		await Assert.That(submit.OnSuccess.Navigate).IsEqualTo("/character/Gandalf");
		await Assert.That(submit.OnError!.BindFieldErrors).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task Section_DeserializesColumnsAndSpan_DefaultingToOne()
	{
		const string json = """
		{ "kind": "view", "schema_version": 1, "pages": [ { "key": "p1", "order": 1, "sections": [
		  { "name": "Grid", "order": 1, "columns": 3, "elements": [
		    { "kind": "field", "key": "a", "type": "text" },
		    { "kind": "field", "key": "wide", "type": "textarea", "span": 3 } ] },
		  { "name": "Stacked", "order": 2, "elements": [ { "kind": "field", "key": "b", "type": "text" } ] }
		] } ] }
		""";

		var doc = JsonSerializer.Deserialize<PortalSchemaDocument>(json, SchemaJson.Options);

		var grid = doc!.Pages![0].Sections![0];
		await Assert.That(grid.Columns).IsEqualTo(3);
		await Assert.That(grid.Elements![0].Span).IsEqualTo(1);   // default
		await Assert.That(grid.Elements![1].Span).IsEqualTo(3);   // explicit

		var stacked = doc.Pages![0].Sections![1];
		await Assert.That(stacked.Columns).IsEqualTo(1);          // default = per-row
	}

	[TUnit.Core.Test]
	public async Task ActionResult_DeserializesErrorEnvelope()
	{
		const string json = """
		{ "ok": false,
		  "errors": { "_global": "Roll failed.", "strength": "Must be 3-18." },
		  "fields": { "strength": 14, "dexterity": 9 },
		  "message": "Try again." }
		""";

		var result = JsonSerializer.Deserialize<SchemaActionResult>(json, SchemaJson.Options);

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Ok).IsFalse();
		await Assert.That(result.Errors!["_global"]).IsEqualTo("Roll failed.");
		await Assert.That(result.Errors!["strength"]).IsEqualTo("Must be 3-18.");
		await Assert.That(result.Fields!["strength"].GetInt32()).IsEqualTo(14);
		await Assert.That(result.Message).IsEqualTo("Try again.");
	}

	[TUnit.Core.Test]
	public async Task ActionResult_WithReturnedSchema_ParsesNestedDocument()
	{
		// Softcode-driven progression: the response carries a replacement schema.
		const string json = """
		{ "ok": true, "schema": { "kind": "form", "schema_version": 1, "title": "Next Step",
		  "pages": [ { "key": "p2", "order": 1, "sections": [] } ] } }
		""";

		var result = JsonSerializer.Deserialize<SchemaActionResult>(json, SchemaJson.Options);

		await Assert.That(result!.Ok).IsTrue();
		await Assert.That(result.Schema).IsNotNull();
		await Assert.That(result.Schema!.Title).IsEqualTo("Next Step");
		await Assert.That(result.Schema.Pages!.Count).IsEqualTo(1);
	}
}
