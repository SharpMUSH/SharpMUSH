using System.Text.Json.Nodes;
using SharpMUSH.Client.Components.Builder;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The schema checker every host shares: what it reports, what it leaves alone, and that the portal draws exactly
/// the vocabulary it checks against.
/// </summary>
public class PortalSchemaTests
{
	private static IReadOnlyList<string> Check(string document, string? data = null) =>
		PortalSchema.Check(JsonNode.Parse(document), data is null ? null : JsonNode.Parse(data));

	[Test]
	public async Task A_sound_document_has_no_problems_and_unknown_keys_are_not_problems()
	{
		var problems = Check("""
			{"kind":"view","schema_version":1,"future_key":true,"actions":{"go":{"route":"http/x"}},
			 "pages":[{"key":"p","order":1,"next":"p","sections":[{"order":1,"columns":2,"elements":[
			   {"kind":"field","key":"name","label":"Name","span":2,"new_setting":"x"},
			   {"kind":"stats","rows_field":"stats"},
			   {"kind":"button","label":"Go","action":"go"}]}]}]}
			""", """{"fields":{"name":{"value":"A"},"stats":{"value":[]}}}""");
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task Each_mistake_is_named_with_its_path()
	{
		var problems = Check("""
			{"kind":"sheet","actions":{"go":{"route":"https://elsewhere.example/x"}},
			 "pages":[{"key":"p","next":"q","sections":[{"columns":2,"elements":[
			   {"kind":"gauge2"},
			   {"kind":"field","type":"dial","key":"a"},
			   {"kind":"field","key":"a"},
			   {"kind":"field","type":"select","key":"b"},
			   {"kind":"field","label":"No key"},
			   {"kind":"button","action":"missing"},
			   {"kind":"table","rows_field":"rows"},
			   {"kind":"chips","src_field":"tags","span":3}]}]}]}
			""", """{"fields":{}}""");

		await Assert.That(problems).IsEquivalentTo(new[]
		{
			"kind: unknown document kind \"sheet\"",
			"actions.go: route \"https://elsewhere.example/x\" is not relative",
			"pages[0]: next names no page \"q\"",
			"pages[0].sections[0].elements[0]: unknown kind \"gauge2\"",
			"pages[0].sections[0].elements[1]: unknown field type \"dial\"",
			"pages[0].sections[0].elements[2]: key \"a\" is used twice",
			"pages[0].sections[0].elements[3]: a select needs options",
			"pages[0].sections[0].elements[4]: a field needs a key",
			"pages[0].sections[0].elements[5]: action \"missing\" is not among the actions",
			"pages[0].sections[0].elements[6]: rows_field \"rows\" is not in the data",
			"pages[0].sections[0].elements[6]: a table needs columns",
			"pages[0].sections[0].elements[7]: span 3 is wider than the section's 2 columns",
			"pages[0].sections[0].elements[7]: src_field \"tags\" is not in the data",
		});
	}

	[Test]
	public async Task Without_data_nothing_is_missing_from_it()
	{
		var problems = Check("""{"kind":"view","pages":[{"sections":[{"elements":[{"kind":"chips","src_field":"tags"}]}]}]}""");
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task Something_that_is_not_a_document_is_said_to_be_so()
	{
		await Assert.That(PortalSchema.Check(JsonNode.Parse("[1]"))).IsEquivalentTo(new[] { "document: not a JSON object" });
		await Assert.That(PortalSchema.Check(null)).IsEquivalentTo(new[] { "document: not a JSON object" });
	}

	[Test]
	public async Task The_portal_draws_the_kinds_the_checker_knows_and_the_builder_offers_them()
	{
		await Assert.That(SharpMUSH.Client.Components.Schema.SchemaDisplay.Kinds).IsEquivalentTo(PortalSchema.DisplayKinds);
		await Assert.That(BuilderDraft.DisplayKinds.Where(kind => !PortalSchema.DisplayKinds.Contains(kind))).IsEmpty();
		await Assert.That(BuilderDraft.FieldTypes.Where(type => !PortalSchema.FieldTypes.Contains(type))).IsEmpty();
	}

	[Test]
	public async Task Every_builder_template_passes_the_check()
	{
		foreach (var template in BuilderTemplates.All)
		{
			var draft = new BuilderDraft { Slug = "t" };
			draft.Load(template.Document());
			await Assert.That(PortalSchema.Check(draft.Stored())).IsEmpty().Because(template.Key);
		}
	}
}
