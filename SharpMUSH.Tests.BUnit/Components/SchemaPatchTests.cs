using System.Text.Json;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>An action reply changes part of a page: <c>replace</c> swaps keyed parts, <c>data_merge</c> lays data over it.</summary>
public class SchemaPatchTests
{
	private static PortalSchemaDocument Sheet => new("form", 1, "Sheet", null,
		[new("main", null, 1, [
			new("Class", 1, null, [new(Kind: "field", Key: "class", Type: "select")], Key: "class"),
			new("Spells", 2, null, [new(Kind: "markdown", Key: "none", Value: "Pick a class first.")], Key: "spells")], null, null)],
		null);

	private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

	[Test]
	public async Task A_section_and_an_element_are_swapped_by_key_and_the_rest_stays()
	{
		var (patched, skipped) = SchemaPatch.Replace(Sheet, [
			new("spells", null, Json("""{"name":"Wizard spells","order":2,"elements":[{"kind":"chips","src_field":"spells"}]}""")),
			new(null, "class", Json("""{"kind":"field","type":"radio","options":[{"value":"wizard","label":"Wizard"}]}""")),
		]);

		var sections = patched.Pages![0].Sections!;
		await Assert.That(skipped).IsEmpty();
		await Assert.That(sections[1].Name).IsEqualTo("Wizard spells");
		await Assert.That(sections[1].Key).IsEqualTo("spells");
		await Assert.That(sections[1].Elements![0].Kind).IsEqualTo("chips");
		await Assert.That(sections[0].Name).IsEqualTo("Class");
		await Assert.That(sections[0].Elements![0].Type).IsEqualTo("radio");
		await Assert.That(sections[0].Elements![0].Key).IsEqualTo("class");
	}

	[Test]
	public async Task A_key_the_page_lacks_or_a_with_that_is_not_an_object_is_skipped()
	{
		var (patched, skipped) = SchemaPatch.Replace(Sheet, [
			new("feats", null, Json("""{"name":"Feats"}""")),
			new(null, "class", Json("[1]")),
		]);

		await Assert.That(skipped).IsEquivalentTo(["feats", "class"]);
		await Assert.That(patched.Pages![0].Sections!.Select(section => section.Name)).IsEquivalentTo(["Class", "Spells"]);
	}

	[Test]
	public async Task Data_merge_replaces_the_keys_it_names_and_keeps_the_others()
	{
		var data = new SchemaData(new Dictionary<string, SchemaFieldValue> { ["hp"] = new(Json("7")), ["name"] = new(Json("\"Ezren\"")) });
		var merged = SchemaPatch.Merge(data, new SchemaData(new Dictionary<string, SchemaFieldValue> { ["hp"] = new(Json("9")) }));

		await Assert.That(merged.Fields!["hp"].Value!.Value.GetInt32()).IsEqualTo(9);
		await Assert.That(merged.Fields["name"].Value!.Value.GetString()).IsEqualTo("Ezren");
	}

	[Test]
	public async Task A_reply_keeps_what_was_typed_while_it_swaps_a_section()
	{
		var state = new SchemaApplicationState();
		state.Load(Sheet with { Actions = new Dictionary<string, SchemaAction> { ["pick"] = new("http", "POST", "act", "fields", null, null) } }, null);
		state.Form.SetString("class", "wizard");
		var token = state.Begin();
		var reply = new SchemaActionResult(true, null, null, null, null, null,
			DataMerge: new SchemaData(new Dictionary<string, SchemaFieldValue> { ["spells"] = new(Json("""["Shield"]""")) }),
			Replace: [new("spells", null, Json("""{"name":"Wizard spells","order":2,"elements":[{"kind":"chips","src_field":"spells"}]}"""))]);

		state.Apply(token, state.Form.Action("pick")!, reply, "failed");

		await Assert.That(state.Form.GetString("class")).IsEqualTo("wizard");
		await Assert.That(state.Document!.Pages![0].Sections![1].Name).IsEqualTo("Wizard spells");
		await Assert.That(state.Data!.Fields!.ContainsKey("spells")).IsTrue();
	}
}
