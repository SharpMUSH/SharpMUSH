using System.Text.Json.Nodes;
using SharpMUSH.Client.Components.Builder;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The application builder's draft without rendering it: what it stores, what it keeps from a loaded document,
/// and what stops a save.
/// </summary>
public class BuilderDraftTests
{
	private static BuilderDraft FromTemplate(string key, string slug = "test")
	{
		var template = BuilderTemplates.Named(key);
		var draft = new BuilderDraft { Slug = slug };
		draft.Load(template.Document());
		foreach (var (k, v) in template.Sources ?? new Dictionary<string, string>()) draft.Sources[k] = v;
		foreach (var (k, v) in template.Save ?? new Dictionary<string, string>()) draft.Save[k] = v;
		return draft;
	}

	[Test]
	public async Task Every_template_previews_and_has_nothing_stopping_a_save()
	{
		foreach (var template in BuilderTemplates.All)
		{
			var draft = FromTemplate(template.Key);
			await Assert.That(draft.Problems()).IsEmpty().Because(template.Key);
			await Assert.That(draft.Preview()).IsNotNull().Because(template.Key);
			await Assert.That(draft.IsForm).IsEqualTo(template.Form).Because(template.Key);
		}
	}

	[Test]
	public async Task A_stored_form_posts_to_the_server_route_and_the_preview_posts_nowhere()
	{
		var draft = FromTemplate("request", "story-ideas");

		var stored = draft.Stored();
		await Assert.That((string?)stored["actions"]!["submit"]!["route"]).IsEqualTo("api/built/story-ideas/act");
		await Assert.That(draft.Preview()!.Actions).IsNull();
		await Assert.That(draft.BaseAttribute).IsEqualTo("DATA`APP`STORY-IDEAS");
	}

	[Test]
	public async Task Required_keys_and_maps_name_only_what_the_document_still_reads()
	{
		var draft = FromTemplate("request");
		draft.Save["gone"] = "REQUEST`GONE";

		await Assert.That(draft.RequiredKeys()).IsEqualTo("subject details");
		var save = JsonNode.Parse(draft.MapJson(draft.Save, draft.InputFields().Select(f => (string)f["key"]!)))!.AsObject();
		await Assert.That(save.Select(p => p.Key)).IsEquivalentTo(["details", "kind", "subject"]);
	}

	[Test]
	public async Task A_loaded_document_keeps_keys_the_builder_has_no_input_for()
	{
		var draft = new BuilderDraft { Slug = "kept" };
		var loaded = draft.LoadStored(
			"""{"kind":"view","schema_version":1,"refresh":30,"pages":[{"key":"p","order":1,"sections":[{"order":1,"elements":[{"kind":"graph","src_field":"map","directed":true}]}]}]}""",
			"""{"map":"MAP`DATA"}""", null);

		await Assert.That(loaded).IsTrue();
		var stored = draft.Stored();
		await Assert.That((int?)stored["refresh"]).IsEqualTo(30);
		await Assert.That((bool?)stored["pages"]![0]!["sections"]![0]!["elements"]![0]!["directed"]).IsEqualTo(true);
		await Assert.That(draft.Sources["map"]).IsEqualTo("MAP`DATA");
	}

	[Test]
	public async Task A_stored_value_that_is_not_a_document_is_refused()
	{
		var draft = new BuilderDraft();
		await Assert.That(draft.LoadStored("not json", null, null)).IsFalse();
		await Assert.That(draft.LoadStored("[1,2]", null, null)).IsFalse();
	}

	[Test]
	public async Task New_elements_take_keys_no_other_element_uses()
	{
		var draft = FromTemplate("blank");
		var section = BuilderDraft.Sections((JsonObject)draft.Pages[0]!).OfType<JsonObject>().First();

		var first = draft.AddElement(section, "text");
		var second = draft.AddElement(section, "text");
		var tiles = draft.AddElement(section, "stats");

		await Assert.That((string?)first["key"]).IsEqualTo("field");
		await Assert.That((string?)second["key"]).IsEqualTo("field2");
		await Assert.That((string?)tiles["rows_field"]).IsEqualTo("stats");
		await Assert.That(draft.DataKeys()).IsEquivalentTo(["field", "field2", "stats"]);
	}

	[Test]
	public async Task Bad_names_stop_a_save()
	{
		var draft = FromTemplate("request", "Bad Slug");
		var field = draft.InputFields().First();
		field["key"] = "Has Space";
		draft.Save["details"] = "NOT AN ATTRIBUTE";

		var problems = draft.Problems();
		await Assert.That(problems.Count).IsEqualTo(3);
	}

	[Test]
	public async Task A_slug_comes_from_the_title()
	{
		await Assert.That(BuilderDraft.SlugFrom("  Story Ideas & Hooks! ")).IsEqualTo("story-ideas-hooks");
		await Assert.That(BuilderDraft.IsSlug("story-ideas")).IsTrue();
		await Assert.That(BuilderDraft.IsSlug("Story")).IsFalse();
	}

	[Test]
	public async Task Sample_data_fills_every_key_the_document_reads()
	{
		var draft = FromTemplate("sheet");
		var sample = draft.SampleData();

		await Assert.That(sample.Fields!.Keys).IsEquivalentTo(draft.DataKeys().ToArray());
	}
}
