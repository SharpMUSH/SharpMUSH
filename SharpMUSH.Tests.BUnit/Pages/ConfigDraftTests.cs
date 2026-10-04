using SharpMUSH.Client.Pages.Admin.Config;
using SharpMUSH.Client.Services;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using PropertyMetadata = SharpMUSH.Library.API.PropertyMetadata;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The config section editor's working copy, without rendering the page: what each edit does to the
/// values, the change count and what a save sends, against the shipped schema and defaults.
/// </summary>
public class ConfigDraftTests
{
	private static readonly ConfigurationSchema Schema = SchemaBuilder.BuildSchema();

	private static PropertyMetadata First(string component) =>
		Schema.Properties.Values.OrderBy(p => p.Path).First(p => p.Component == component && !p.ReadOnly);

	private static ConfigDraft Loaded(params PropertyMetadata[] properties)
	{
		var draft = new ConfigDraft();
		draft.Load(properties, SharpMUSHOptions.Default());
		return draft;
	}

	[Test]
	public async Task Loading_HoldsNoChanges()
	{
		var draft = Loaded(First("switch"), First("stringlist"));

		await Assert.That(draft.HasChanges).IsFalse();
		await Assert.That(draft.Changes()).IsEmpty();
	}

	[Test]
	public async Task ToggleASwitch_CountsOneChange_AndToggleBack_CountsNone()
	{
		var property = First("switch");
		var draft = Loaded(property);
		var on = draft.Bool(property);

		draft.Set(property, !on);
		await Assert.That(draft.ChangedCount).IsEqualTo(1);
		await Assert.That(draft.IsChanged(property.Path)).IsTrue();
		await Assert.That(draft.Changes().Keys).Contains(property.Path);

		draft.Set(property, on);
		await Assert.That(draft.ChangedCount).IsEqualTo(0);
		await Assert.That(draft.IsChanged(property.Path)).IsFalse();
	}

	[Test]
	public async Task Numeric_ChangesOnlyWhenTheTextParses()
	{
		var property = First("numeric");
		var draft = Loaded(property);
		var before = draft.NumericString(property);

		draft.SetNumeric(property, "-");
		await Assert.That(draft.NumericString(property)).IsEqualTo(before);
		await Assert.That(draft.HasChanges).IsFalse();
	}

	[Test]
	public async Task ListDraft_CommitsTrimmed_IgnoresBlankAndRepeats_AndEmptiesTheBox()
	{
		var property = First("stringlist");
		var draft = Loaded(property);
		var count = draft.StringList(property).Count;

		draft.SetListDraft(property, "  sharp-draft-item  ");
		draft.CommitListDraft(property);
		await Assert.That(draft.StringList(property)).Contains("sharp-draft-item");
		await Assert.That(draft.ListDraft(property)).IsEqualTo(string.Empty);

		draft.SetListDraft(property, "sharp-draft-item");
		draft.CommitListDraft(property);
		draft.SetListDraft(property, "   ");
		draft.CommitListDraft(property);
		await Assert.That(draft.StringList(property).Count).IsEqualTo(count + 1);

		draft.RemoveListItemAt(property, draft.StringList(property).IndexOf("sharp-draft-item"));
		await Assert.That(draft.StringList(property).Count).IsEqualTo(count);
	}

	[Test]
	public async Task DictDraft_WritesBackAsADictionary_DroppingBlankKeys()
	{
		var property = First("dictionary");
		var draft = Loaded(property);

		draft.AddDictEntry(property);
		var index = draft.DictEntries(property).Count - 1;
		await Assert.That(((Dictionary<string, string[]>)draft.Values[property.Path]!).ContainsKey(string.Empty)).IsFalse();

		draft.SetDictKey(property, index, "sharp-key");
		draft.DictEntries(property)[index].ValueDraft = " one ";
		draft.CommitDictValueDraft(property, index);
		draft.DictEntries(property)[index].ValueDraft = "one";
		draft.CommitDictValueDraft(property, index);

		var written = (Dictionary<string, string[]>)draft.Values[property.Path]!;
		await Assert.That(written["sharp-key"]).IsEquivalentTo(new[] { "one" });
		await Assert.That(draft.DictEntries(property)[index].ValueDraft).IsEqualTo(string.Empty);

		draft.RemoveDictValueAt(property, index, 0);
		await Assert.That(((Dictionary<string, string[]>)draft.Values[property.Path]!)["sharp-key"]).IsEmpty();

		draft.RemoveDictEntry(property, index);
		await Assert.That(((Dictionary<string, string[]>)draft.Values[property.Path]!).ContainsKey("sharp-key")).IsFalse();
	}

	[Test]
	public async Task ResetChanges_RestoresTheLoadedValues_AndDropsRefusals()
	{
		var property = First("switch");
		var draft = Loaded(property);
		var on = draft.Bool(property);
		draft.Set(property, !on);
		draft.SetErrors(new Dictionary<string, string?> { [property.Path] = null }, "Invalid");

		draft.ResetChanges();

		await Assert.That(draft.Bool(property)).IsEqualTo(on);
		await Assert.That(draft.HasChanges).IsFalse();
		await Assert.That(draft.TryGetError(property.Path, out _)).IsFalse();
	}

	[Test]
	public async Task AnEdit_ClearsTheRefusalStandingAgainstIt()
	{
		var property = First("switch");
		var draft = Loaded(property);
		draft.SetErrors(new Dictionary<string, string?> { [property.Path] = null }, "Invalid");

		await Assert.That(draft.TryGetError(property.Path, out var error)).IsTrue();
		await Assert.That(error).IsEqualTo("Invalid");

		draft.Set(property, !draft.Bool(property));
		await Assert.That(draft.TryGetError(property.Path, out _)).IsFalse();
	}

	[Test]
	public async Task MarkSaved_MakesTheSavedValuesTheLoadedOnes()
	{
		var property = First("switch");
		var draft = Loaded(property);
		draft.Set(property, !draft.Bool(property));

		draft.MarkSaved(draft.Changes().Keys);

		await Assert.That(draft.HasChanges).IsFalse();
		await Assert.That(draft.IsChanged(property.Path)).IsFalse();
	}

	[Test]
	public async Task LoadingAnotherCategory_DropsTheEditsOfTheLast()
	{
		var first = First("switch");
		var draft = Loaded(first);
		draft.Set(first, !draft.Bool(first));

		draft.Load([First("stringlist")], SharpMUSHOptions.Default());

		await Assert.That(draft.HasChanges).IsFalse();
		await Assert.That(draft.Changes()).IsEmpty();
	}

	[Test]
	public async Task ConfigNaming_TitlesAndAnchors()
	{
		await Assert.That(ConfigNaming.CategoryName("sitelock")).IsEqualTo("SitelockRules");
		await Assert.That(ConfigNaming.CategoryName("widgets")).IsEqualTo("Widgets");
		await Assert.That(ConfigNaming.CategoryTitle("TextFile")).IsEqualTo("Text File");
		await Assert.That(ConfigNaming.PropertyTitle("max_depth")).IsEqualTo("Max Depth");
		await Assert.That(ConfigNaming.PropertyTitle("depth")).IsEqualTo("Depth");
		await Assert.That(ConfigNaming.PropertyTitle("MaxDepth")).IsEqualTo("Max Depth");
		await Assert.That(ConfigNaming.GroupAnchor("Chat & Mail")).IsEqualTo("group-chat-mail");
		await Assert.That(ConfigNaming.RangeText(1, 10)).IsEqualTo("1–10");
		await Assert.That(ConfigNaming.RangeText(1, null)).IsEqualTo("≥1");
		await Assert.That(ConfigNaming.RangeText(null, null)).IsNull();
	}

	[Test]
	public async Task Refusal_ReadsFieldsAndGlobals()
	{
		var refusal = ConfigSaveRefusal.Parse("""{ "errors": { "Chat.Max": "too big", "_global": "no" } }""")
			.Expect<ConfigSaveRefusal>();

		await Assert.That(refusal.Fields["Chat.Max"]).IsEqualTo("too big");
		await Assert.That(refusal.Messages).IsEquivalentTo(new string?[] { "no" });
	}

	[Test]
	public async Task Refusal_ReadsAStringErrorsAsOneMessage()
	{
		var refusal = ConfigSaveRefusal.Parse("""{ "errors": "No updates provided" }""").Expect<ConfigSaveRefusal>();

		await Assert.That(refusal.Fields).IsEmpty();
		await Assert.That(refusal.Messages).IsEquivalentTo(new string?[] { "No updates provided" });
	}

	[Test]
	[Arguments("")]
	[Arguments("plain text")]
	[Arguments("""{ "detail": "x" }""")]
	public async Task Refusal_IsNotFound_ForAnyOtherBody(string body)
	{
		await Assert.That(ConfigSaveRefusal.Parse(body) is NotFound).IsTrue();
	}
}
