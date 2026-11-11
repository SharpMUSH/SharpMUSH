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
	public async Task AKey_IsFoundWhateverItsCasing_AndWrittenBackInPlaceUnderItsSchemaName()
	{
		var doc = Doc("""{"first":1,"Slug":"old","last":2}""");
		await Assert.That(doc.GetString("slug")).IsEqualTo("old");

		doc.SetString("slug", "new");

		await Assert.That(doc.ToJson()!.Replace(" ", "").Replace("\n", "")).IsEqualTo("""{"first":1,"slug":"new","last":2}""");
	}

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
		await Assert.That(Doc("""{"SLUG":"a","extra":1,"locale":"de"}""").UnknownKeys(fields)).IsEquivalentTo(["extra"]);
	}
}
