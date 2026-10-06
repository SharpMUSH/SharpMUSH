using SharpMUSH.Client.Pages.Admin.Config;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>The MSSP page's unsaved edits, apart from the page.</summary>
public class MsspDraftTests
{
	/// <summary>
	/// Two other variables spelt <c>foo</c> and <c>FOO</c> are one name: the draft counts its changes
	/// (which the page does on every render) and files the second row as a duplicate.
	/// </summary>
	[Test]
	public async Task TwoSpellingsOfOneOtherVariableAreOneNameAndADuplicate()
	{
		var draft = new MsspDraft();
		draft.Load(new Dictionary<string, string[]>());
		var first = new MsspOtherVariable { Name = "foo" };
		first.Values.Add("one");
		var second = new MsspOtherVariable { Name = "FOO" };
		second.Values.Add("two");
		draft.Others.Add(first);
		draft.Others.Add(second);

		await Assert.That(draft.ChangedCount).IsEqualTo(1);
		await Assert.That(draft.Settings().Keys.ToArray()).IsEquivalentTo(new[] { "FOO" });
		await Assert.That(draft.Problems(name => name)[draft.OtherKey(second)]).IsEqualTo("FOO is given twice.");
	}
}
