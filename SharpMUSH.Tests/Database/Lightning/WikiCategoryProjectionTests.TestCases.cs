using System.Text;
using System.Text.Json;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class WikiCategoryProjectionTests
{
	private static readonly string[] StringMembers = new[] { "Slug", "Title", "Namespace", "MarkdownSource", "RenderedHtml", "PlainText", "AuthorDbref", "LastEditorDbref", "CreatedAt", "UpdatedAt", "SourceLocale" };

	public static IEnumerable<Func<(string Name, byte[] Bytes, string Expected)>> DifferentialCases()
	{
		var cases = new List<(string Name, byte[] Bytes, string Expected)>();
		void Json(string name, string json, string expected = "record") => cases.Add((name, Encoding.UTF8.GetBytes(json), expected));
		void Invalid(string name, string prefix, string suffix, string expected = "record") => cases.Add((name, [.. Encoding.UTF8.GetBytes(prefix), 0xFF, .. Encoding.UTF8.GetBytes(suffix)], expected));
		Json("ordinary", "{\"Title\":\"Guide\",\"Categories\":[\"A\",\"a\"]}");
		Json("with-facts", "{\"Title\":\"Guide\",\"Categories\":[\"A\"],\"Facts\":[{\"Property\":\"pf2e:level\",\"Value\":\"3\"}]}");
		Invalid("raw-invalid-known-body", "{\"MarkdownSource\":\"", "\"}", "reject");
		Invalid("raw-invalid-unknown-value", "{\"unknown\":\"", "\"}");
		Invalid("raw-invalid-root-name", "{\"", "\":0}");
		Invalid("raw-invalid-nested-name", "{\"unknown\":{\"", "\":0}}");
		Invalid("raw-invalid-nested-value", "{\"unknown\":{\"nested\":\"", "\"}}");
		foreach (var surrogate in new[] { "\\uD800", "\\uDC00" })
		{
			Json("escaped-known-body-" + surrogate, "{\"MarkdownSource\":\"" + surrogate + "\"}", "reject");
			Json("escaped-unknown-value-" + surrogate, "{\"unknown\":\"" + surrogate + "\"}");
			Json("escaped-root-name-" + surrogate, "{\"" + surrogate + "\":0}", "reject");
			Json("escaped-nested-name-" + surrogate, "{\"unknown\":{\"" + surrogate + "\":0}}");
		}
		Json("escaped-known-name", "{\"\\u0054itle\":\"Guide\"}");
		Json("invalid-known-overwritten", "{\"Title\":5,\"Title\":\"Guide\"}", "reject");
		foreach (var depth in new[] { 63, 64, 65 })
			Json("depth-" + depth, "{\"unknown\":" + new string('[', depth - 1) + "0" + new string(']', depth - 1) + "}", depth == 65 ? "reject" : "record");
		Json("missing-defaults", "{}");
		Json("root-null", "null", "null");
		Json("empty-categories", "{\"Categories\":[]}");
		Json("null-category-element", "{\"Categories\":[null,\"\",null]}");
		Json("duplicates-final", "{\"Title\":\"old\",\"Title\":null,\"AuthorDbref\":null,\"AuthorDbref\":\"#9\",\"Categories\":[null],\"Categories\":[\"b\",\"B\",\"a\",\"A\"],\"Published\":true,\"Published\":false}");
		Json("case-sensitive-names", "{\"title\":5,\"categories\":false}");
		Json("escaped-all-projected-names", "{\"\\u0054itle\":\"A\\u0000😀\",\"\\u0041uthorDbref\":\"#2\",\"\\u0050ublished\":true,\"\\u0043ategories\":[\"\\u0041\"]}");
		Json("unicode-fold", JsonSerializer.Serialize(new { Title = "😀\0é", AuthorDbref = "#1", Categories = new[] { "σ", "ς", "Σ", "i", "ı", "İ", "ß", "SS", "é", "e\u0301", "😀", "\0", "" } }));
		Json("many-categories", JsonSerializer.Serialize(new { Categories = Enumerable.Range(0, 257).Select(i => "c" + i).Concat(new[] { "C0", "c128" }).ToArray() }));
		Json("large-body", JsonSerializer.Serialize(new { MarkdownSource = new string('x', 131073), Categories = new[] { "a" } }));
		var strings = StringMembers;
		foreach (var member in strings)
		{
			Json("null-" + member, "{\"" + member + "\":null}");
			Json("type-" + member, "{\"" + member + "\":17}", "reject");
		}
		Json("null-Categories", "{\"Categories\":null}");
		Json("null-Published", "{\"Published\":null}");
		Json("null-RevisionNumber", "{\"RevisionNumber\":null}", "reject");
		foreach (var item in new[] { ("Categories", "{}"), ("Categories", "[1]"), ("Published", "0"), ("RevisionNumber", "true") })
			Json("type-" + item.Item1 + "-" + item.Item2, "{\"" + item.Item1 + "\":" + item.Item2 + "}", "reject");
		foreach (var json in new[] { "{} {}", "[]", "true", "{\"Title\":\"a\",}", "{/*x*/}", "{", "{\"Categories\":[\"\\uD800\"]}" })
			Json("syntax-" + cases.Count, json, "reject");
		// New cases deliberately derive acceptance from the live Codec; root freezes observed counts.
		foreach (var number in new[] { "-2147483648", "2147483647", "-2147483649", "2147483648", "1.0", "1e0", "1e1", "\"1\"", "0", "-0", "1.5", "1e100" })
			Json("revision-number-" + number, "{\"RevisionNumber\":" + number + "}", "oracle");
		foreach (var pair in new[] { ("Title", "[]", "\"valid\""), ("Published", "\"true\"", "true"), ("Categories", "[\"a\",17]", "[]"), ("Categories", "{}", "[\"valid\"]"), ("RevisionNumber", "null", "1"), ("MarkdownSource", "false", "\"valid\"") })
			Json("invalid-earlier-" + pair.Item1 + "-" + pair.Item2, "{\"" + pair.Item1 + "\":" + pair.Item2 + ",\"" + pair.Item1 + "\":" + pair.Item3 + "}", "oracle");
		Json("long-escaped-unknown-root", "{\"" + string.Concat(Enumerable.Repeat("\\u0061", 16385)) + "\":0,\"Title\":\"final\"}", "oracle");
		Invalid("raw-invalid-root-mixed-valid-escape", "{\"prefix\\u0041", "\\u0042\":0}", "oracle");
		Invalid("raw-invalid-root-mixed-lone-surrogate", "{\"prefix", "\\uD800\":0}", "oracle");
		Invalid("raw-invalid-nested-mixed-lone-surrogate", "{\"unknown\":{\"prefix", "\\uD800\":0}}", "oracle");
		foreach (var depth in new[] { 63, 64, 65 })
		{
			var nested = "0";
			for (var i = 1; i < depth; i++) nested = i % 2 == 0 ? "{\"n\":" + nested + "}" : "[" + nested + "]";
			Json("mixed-depth-" + depth, "{\"unknown\":" + nested + "}", "oracle");
		}
		var bmp = Enumerable.Range(0, 65536).Where(i => i is < 0xD800 or > 0xDFFF).Select(i => ((char)i).ToString()).ToArray();
		Json("all-valid-bmp-single-char-categories", JsonSerializer.Serialize(new { Categories = bmp }), "oracle");
		Json("supplementary-and-prefix-fold", JsonSerializer.Serialize(new { Categories = new[] { "𐐀", "𐐨", "𐒰", "𐓘", "𞤀", "𞤢", "Aσ", "aς", "prefixİ", "PREFIXi", "éA", "Éa", "😀a", "😀A" } }), "oracle");
		foreach (var member in strings)
			Json("escapes-controls-" + member, "{\"" + member + "\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0000\\u001f\\uD83D\\uDE00\"}", "oracle");
		Json("escapes-controls-category", "{\"Categories\":[\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0000\\u001f\\uD83D\\uDE00\"]}", "oracle");
		foreach (var literal in new[] { "truX", "falsX", "nulX" })
		{
			Json("malformed-literal-root-" + literal, literal, "reject");
			Json("malformed-literal-nested-" + literal, "{\"unknown\":" + literal + "}", "reject");
		}
		foreach (var literal in new[] { "truX", "falsX", "nulX" })
		{
			Json("quoted-bad-literal-known-" + literal, JsonSerializer.Serialize(new { Title = literal }), "record");
			Json("quoted-bad-literal-unknown-" + literal, "{\"unknown\":\"" + literal + "\"}", "record");
			Json("quoted-escaped-delimiters-" + literal, JsonSerializer.Serialize(new { Title = "\\\"" + literal + "\"\\" }), "record");
			Json("unicode-escaped-quotes-" + literal, "{\"Title\":\"\\u0022" + literal + "\\u005c\\u0022\"}", "record");
			Invalid("raw-invalid-unknown-quoted-literal-" + literal, "{\"unknown\":\"" + literal, literal + "\"}", "record");
		}
		// TUnit invokes each factory with independently owned mutable input.
		return cases.Select(item => (Func<(string Name, byte[] Bytes, string Expected)>)(
			() => (item.Name, (byte[])item.Bytes.Clone(), item.Expected)));
	}

	public static IEnumerable<string> AuxiliaryCases() =>
	[
		"schema-member-serializer-guard",
		"pre-cancel",
		"budget-source-admission",
		"allocation-smoke-large-ignored-body",
		"allocation-smoke-repeated-known-strings",
		"allocation-smoke-repeated-tiny-categories",
		"malformed-literal-private-tail-allocation-truX-root",
		"malformed-literal-private-tail-allocation-truX-nested",
		"malformed-literal-private-tail-allocation-falsX-root",
		"malformed-literal-private-tail-allocation-falsX-nested",
		"malformed-literal-private-tail-allocation-nulX-root",
		"malformed-literal-private-tail-allocation-nulX-nested",
		"admission-independent-charge-model",
		"admission-exact-peak",
		"admission-initial-minus-one",
		"admission-peak-minus-one",
		"capacity-precedes-malformed-preflight",
		"dispose-full-access-matrix",
	];
}
