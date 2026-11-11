using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The layout editor's settings form edits a widget's config through <see cref="WidgetConfigDocument"/>:
/// one key at a time, leaving every other key as it was.
/// </summary>
public class WidgetConfigDocumentTests
{
	private static WidgetConfigDocument Doc(string? json)
		=> WidgetConfigDocument.Parse(json, "not an object") switch
		{
			WidgetConfigDocument document => document,
			Error<string> error => throw new InvalidOperationException(error.Value),
		};

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("  ")]
	[Arguments("{}")]
	public async Task NothingStored_IsNoConfig(string? json)
		=> await Assert.That(Doc(json).ToJson()).IsNull();

	[Test]
	[Arguments("[1]", "not an object")]
	[Arguments("42", "not an object")]
	[Arguments("null", "not an object")]
	public async Task ANonObject_IsRefused(string json, string message)
	{
		var refused = WidgetConfigDocument.Parse(json, "not an object") is Error<string> error ? error.Value : null;
		await Assert.That(refused).IsEqualTo(message);
	}

	[Test]
	public async Task MalformedJson_IsRefusedWithTheParsersMessage()
		=> await Assert.That(WidgetConfigDocument.Parse("{nope", "not an object") is Error<string> { Value.Length: > 0 } error
			&& error.Value != "not an object").IsTrue();

	[Test]
	public async Task AKey_IsMatchedByItsExactName_AsTheWidgetsReadIt()
	{
		// Widgets deserialize camelCase names exactly: "Slug" is a key they never see.
		var doc = Doc("""{"first":1,"Slug":"old","last":2}""");
		await Assert.That(doc.GetString("slug")).IsNull();

		doc.SetString("slug", "new");

		await Assert.That(doc.ToJson()!.Replace(" ", "").Replace("\n", ""))
			.IsEqualTo("""{"first":1,"Slug":"old","last":2,"slug":"new"}""");
	}

	[Test]
	public async Task TheJson_KeepsTextReadable()
	{
		var doc = Doc("""{"markdown":"Café <b>don't</b> & 世界"}""");
		await Assert.That(doc.ToJson()).Contains("Café <b>don't</b> & 世界");
	}

	[Test]
	public async Task AnItemLeftEmpty_IsDroppedOnSave_WithAListItEmptied()
	{
		var doc = Doc("""{"links":[{"label":"A"}],"other":[]}""");
		doc.AddItem("links");
		doc.AddItem("more");

		await Assert.That(doc.ToJson()!.Replace(" ", "").Replace("\n", "")).IsEqualTo("""{"links":[{"label":"A"}]}""");
	}

	[Test]
	[Arguments(null, true)]
	[Arguments("""{"tags":["a","b"]}""", true)]
	[Arguments("""{"tags":[{"x":1}]}""", false)]
	[Arguments("""{"tags":[1]}""", false)]
	[Arguments("""{"tags":"a"}""", false)]
	public async Task OnlyAListOfStrings_IsEditedOnePerLine(string? json, bool lines)
		=> await Assert.That(Doc(json).HoldsLines("tags")).IsEqualTo(lines);

	[Test]
	public async Task ClearingAField_RemovesItsKey()
	{
		var doc = Doc("""{"slug":"a","height":4,"tags":["x"]}""");
		doc.SetString("slug", "");
		doc.SetNumber("height", null, integer: true);
		doc.SetLines("tags", ["", "  "]);

		await Assert.That(doc.ToJson()).IsNull();
	}

	[Test]
	public async Task AnIntegerKey_IsWrittenAsAnInteger()
	{
		var doc = Doc(null);
		doc.SetNumber("height", 24.4, integer: true);
		doc.SetNumber("ratio", 1.5, integer: false);

		await Assert.That(doc.ToJson()!.Replace(" ", "").Replace("\n", "")).IsEqualTo("""{"height":24,"ratio":1.5}""");
		await Assert.That(doc.GetNumber("height")).IsEqualTo(24);
	}

	[Test]
	public async Task ABoolean_ReadsOnlyARealBoolean()
	{
		var doc = Doc("""{"on":true,"text":"true"}""");
		await Assert.That(doc.GetBool("on")).IsTrue();
		await Assert.That(doc.GetBool("text")).IsNull();
		await Assert.That(doc.GetBool("missing")).IsNull();
	}

	[Test]
	public async Task ListItems_AreEditedInPlace_AndMoveWithinTheList()
	{
		var doc = Doc("""{"links":[{"label":"A"},"stray",{"label":"B"}]}""");
		var items = doc.GetItems("links");
		await Assert.That(items.Count).IsEqualTo(2).Because("an entry that is not an object is not a form item");

		items[1].SetString("label", "B2");
		doc.MoveItem("links", items[1], -1);
		doc.MoveItem("links", items[0], -5);

		await Assert.That(doc.ToJson()!.Replace(" ", "").Replace("\n", ""))
			.IsEqualTo("""{"links":[{"label":"A"},{"label":"B2"},"stray"]}""");
	}

	[Test]
	public async Task UnknownKeys_AreTheOnesNoFieldNames()
	{
		var fields = SharpMUSH.Library.Models.Portal.Widgets.WidgetConfigSchema.Describe(typeof(WikiBodyConfig));
		await Assert.That(Doc("""{"SLUG":"a","extra":1,"locale":"de"}""").UnknownKeys(fields)).IsEquivalentTo(["SLUG", "extra"]);
	}
}
