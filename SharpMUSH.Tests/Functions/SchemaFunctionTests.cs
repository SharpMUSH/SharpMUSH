using System.Text;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Functions;

public class SchemaFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private const string Sheet = """
		{"kind":"view","title":"Story sheet","layout":"tabs","pages":[
		 {"key":"main","title":"Main","sections":[{"name":"About","columns":2,"elements":[
		  {"kind":"keyvalue","fields":["concept","likes"]},
		  {"kind":"stats","rows_field":"stats"},
		  {"kind":"chips","src_field":"tags"},
		  {"kind":"table","rows_field":"gear","columns":[{"key":"name","label":"Name"},{"key":"qty","label":"Qty"}]}]}]},
		 {"key":"log","title":"Log","sections":[{"elements":[{"kind":"timeline","rows_field":"log"}]}]}]}
		""";

	private const string Data = """
		{"fields":{
		 "concept":{"value":"A thief","visible":true},
		 "likes":{"value":["cats","rain"],"visible":true},
		 "stats":{"value":[{"label":"Luck","value":"2","current":2,"max":3,"pips":true}],"visible":true},
		 "tags":{"value":["sneaky","quick"],"visible":true},
		 "gear":{"value":[{"name":"Rope","qty":1}],"visible":true},
		 "log":{"value":[{"author":"GM","time":0,"body":"Met the duke."}],"visible":true}}}
		""";

	/// <summary>A JSON argument passed through <c>decode64()</c>, so the parser does not read its braces.</summary>
	private static string Encoded(string json) => $"decode64({Convert.ToBase64String(Encoding.UTF8.GetBytes(json))})";

	private async Task<string> Evaluate(string call) => (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(call))).ToPlainText();

	[Test]
	public async Task A_sheet_draws_the_first_tab_and_names_the_others()
	{
		var drawn = await Evaluate($"schemarender({Encoded(Sheet)},{Encoded(Data)},{Encoded("""{"width":78}""")})");

		await Assert.That(drawn).Contains("Story sheet · Main");
		await Assert.That(drawn).Contains("A thief");
		await Assert.That(drawn).Contains("cats, rain");
		await Assert.That(drawn).Contains("●●○ 2");
		await Assert.That(drawn).Contains("sneaky · quick");
		await Assert.That(drawn).Contains("Rope");
		await Assert.That(drawn).Contains("Other pages");
		await Assert.That(drawn).DoesNotContain("Met the duke.");
	}

	[Test]
	public async Task A_named_page_is_the_one_drawn()
	{
		var drawn = await Evaluate($"schemarender({Encoded(Sheet)},{Encoded(Data)},{Encoded("""{"width":78,"page":"log"}""")})");

		await Assert.That(drawn).Contains("Story sheet · Log");
		await Assert.That(drawn).Contains("GM 1970-01-01 00:00");
		await Assert.That(drawn).Contains("Met the duke.");
		await Assert.That(drawn).DoesNotContain("A thief");
	}

	[Test]
	public async Task A_hidden_datum_is_not_drawn()
	{
		var hidden = Data.Replace("""{"value":"A thief","visible":true}""", """{"value":"A thief","visible":false}""");
		var drawn = await Evaluate($"schemarender({Encoded(Sheet)},{Encoded(hidden)})");

		await Assert.That(drawn).DoesNotContain("A thief");
		await Assert.That(drawn).Contains("cats, rain");
	}

	[Test]
	[Arguments("nope", ErrorMessages.Returns.InvalidSchema)]
	public async Task Something_that_is_not_a_document_is_refused(string schema, string expected) =>
		await Assert.That(await Evaluate($"schemarender({schema},)")).IsEqualTo(expected);

	[Test]
	public async Task Data_that_is_not_an_object_is_refused() =>
		await Assert.That(await Evaluate($"schemarender({Encoded(Sheet)},{Encoded("[1]")})")).IsEqualTo(ErrorMessages.Returns.InvalidSchemaData);

	[Test]
	public async Task A_sound_sheet_has_no_problems() =>
		await Assert.That(await Evaluate($"schemacheck({Encoded(Sheet)},{Encoded(Data)})")).IsEqualTo(string.Empty);

	[Test]
	public async Task Each_problem_is_a_line_with_its_path()
	{
		var broken = Sheet.Replace("\"kind\":\"chips\"", "\"kind\":\"bogus\"").Replace("\"rows_field\":\"gear\"", "\"rows_field\":\"kit\"");
		var problems = (await Evaluate($"schemacheck({Encoded(broken)},{Encoded(Data)})")).Split('\n');

		await Assert.That(problems).IsEquivalentTo([
			"pages[0].sections[0].elements[2]: unknown kind \"bogus\"",
			"pages[0].sections[0].elements[3]: rows_field \"kit\" is not in the data",
		]);
	}

	[Test]
	public async Task Without_data_nothing_is_missing()
	{
		var renamed = Sheet.Replace("\"rows_field\":\"gear\"", "\"rows_field\":\"kit\"");
		await Assert.That(await Evaluate($"schemacheck({Encoded(renamed)})")).IsEqualTo(string.Empty);
	}
}
