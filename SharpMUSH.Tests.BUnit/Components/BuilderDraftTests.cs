using System.Text.Json.Nodes;
using SharpMUSH.Client.Components.Builder;
using SharpMUSH.Library.Models.Portal.Applications;

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

	[Test]
	public async Task A_reopened_widget_registers_again_where_it_was_with_only_its_title_changed()
	{
		var registered = FromTemplate("sheet", "finger").Registration("#12:345", true, "card", null) with
		{
			Zones = ["Sidebar"],
			Icon = "badge",
			Order = 7
		};

		var (widget, scope) = BuilderDraft.Placement(registered);
		var reopened = FromTemplate("sheet", "finger");
		reopened.Title = "Finger, again";
		var again = reopened.Registration("#12:345", widget, scope, registered);

		await Assert.That(again.KindEnum).IsEqualTo(ApplicationKind.Widget);
		await Assert.That(again.Scope).IsEqualTo("card");
		await Assert.That(again.Zones).IsEquivalentTo(["Sidebar"]);
		await Assert.That(again.DataUrl).IsEqualTo("api/built/finger/data?objid={objid}");
		await Assert.That(again.Icon).IsEqualTo("badge");
		await Assert.That(again.Order).IsEqualTo(7);
		await Assert.That(again.DisplayName).IsEqualTo("Finger, again");
	}

	[Test]
	public async Task A_widget_for_any_scope_and_a_page_reopen_as_they_were()
	{
		var anywhere = FromTemplate("sheet", "finger").Registration("#12:345", true, "home", null);
		var page = FromTemplate("sheet", "finger").Registration("#12:345", false, "profile", null);

		await Assert.That(anywhere.Scope).IsNull();
		await Assert.That(BuilderDraft.Placement(anywhere)).IsEqualTo((true, "home"));
		await Assert.That(BuilderDraft.Placement(page).Widget).IsFalse();
		await Assert.That(page.DataUrl).IsEqualTo("api/built/finger/data");
	}

	[Test]
	public async Task A_problem_names_the_element_it_is_about_and_the_step_that_fixes_it()
	{
		var draft = FromTemplate("request");
		var kind = (JsonObject)BuilderDraft.Elements(BuilderDraft.Sections((JsonObject)draft.Pages[0]!)[0]!.AsObject())[1]!;
		kind["options"] = new JsonArray();
		draft.Sources["subject"] = "not an attribute!";

		var issues = draft.Issues();
		var choices = issues.Single(issue => issue.Text.Contains("has no choices"));
		await Assert.That(choices.Node).IsSameReferenceAs(kind);
		await Assert.That(choices.Step).IsEqualTo(BuilderIssue.LayoutStep);
		await Assert.That(issues.Single(issue => issue.Text.Contains("not an attribute name")).Step).IsEqualTo(BuilderIssue.ValuesStep);
	}

	[Test]
	public async Task A_new_label_renames_a_key_that_follows_it_and_leaves_one_that_does_not()
	{
		var draft = FromTemplate("request");
		var fields = draft.InputFields().ToList();
		var subject = fields.Single(f => (string?)f["key"] == "subject");
		var kind = fields.Single(f => (string?)f["key"] == "kind");

		draft.SetLabel(subject, "Your Title");
		draft.SetLabel(kind, "Topic");
		draft.SetLabel(fields.Single(f => (string?)f["key"] == "details"), "Your title");

		await Assert.That((string?)subject["key"]).IsEqualTo("your_title");
		await Assert.That((string?)kind["key"]).IsEqualTo("kind");
		await Assert.That(draft.InputFields().Select(f => (string)f["key"]!).Distinct().Count()).IsEqualTo(3);
	}

	[Test]
	public async Task A_snapshot_restores_the_document_and_its_maps()
	{
		var draft = FromTemplate("profile", "about-me");
		var snapshot = draft.Snapshot();
		draft.Pages.Clear();
		draft.Slug = "other";
		draft.Sources.Clear();

		await Assert.That(draft.Restore(snapshot)).IsTrue();
		await Assert.That(draft.Slug).IsEqualTo("about-me");
		await Assert.That(draft.Sources["likes"]).IsEqualTo("PROFILE`LIKES");
		await Assert.That(draft.InputFields().Count()).IsEqualTo(4);
		await Assert.That(draft.Restore("not json")).IsFalse();
		await Assert.That(draft.Slug).IsEqualTo("about-me");
	}

	[Test]
	public async Task A_duplicated_section_sits_after_the_original_with_keys_of_its_own()
	{
		var draft = FromTemplate("request");
		var sections = BuilderDraft.Sections((JsonObject)draft.Pages[0]!);
		var copy = draft.Duplicate(sections, (JsonObject)sections[0]!);

		await Assert.That(sections.IndexOf(copy)).IsEqualTo(1);
		await Assert.That(draft.InputFields().Select(f => (string)f["key"]!).Distinct().Count()).IsEqualTo(6);
		await Assert.That(draft.Problems()).IsEmpty();
		await Assert.That(draft.PathOf(BuilderDraft.Elements(copy)[2]!.AsObject())).IsEqualTo((0, 1, 2));
	}

	[Test]
	public async Task A_stored_document_keeps_its_own_actions_and_where_it_is_kept_through_an_undo()
	{
		const string document = """
			{"kind":"form","schema_version":1,"pages":[{"key":"main","order":1,"sections":[{"name":"","order":1,
			"elements":[{"kind":"field","key":"name","label":"Name","type":"text","visible_to":"public"}]}]}],
			"actions":{"submit":{"transport":"http","method":"POST","route":"http/chargen/act","payload":"fields"}}}
			""";
		var draft = new BuilderDraft { Slug = "chargen" };
		await Assert.That(draft.LoadStored(document, null, null, "DATA`CHARGEN`SCHEMA")).IsTrue();

		var stored = draft.Stored();
		await Assert.That((string?)stored["actions"]!["submit"]!["route"]).IsEqualTo("http/chargen/act");
		await Assert.That((string?)stored["pages"]![0]!["sections"]![0]!["elements"]![0]!["visible_to"]).IsEqualTo("public");
		await Assert.That(draft.BaseAttribute).IsEqualTo("DATA`CHARGEN`SCHEMA");

		var restored = new BuilderDraft();
		await Assert.That(restored.Restore(draft.Snapshot())).IsTrue();
		await Assert.That(restored.KeptAt).IsEqualTo("DATA`CHARGEN`SCHEMA");

		restored.Load(new JsonObject());
		await Assert.That(restored.KeptAt).IsNull();
	}
}
