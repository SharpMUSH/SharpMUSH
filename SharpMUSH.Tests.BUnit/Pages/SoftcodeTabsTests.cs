using SharpMUSH.Client.Models;
using SharpMUSH.Client.Pages;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The Softcode Editor's open tabs without rendering the page: opening, switching, closing, and which
/// tabs hold unsaved edits through save and refresh.
/// </summary>
public class SoftcodeTabsTests
{
	private static MushObject Obj(int dbref, params (string Name, string Value)[] attributes) => new()
	{
		Dbref = dbref,
		Name = $"Object{dbref}",
		Attributes = attributes.Select(a => new MushAttribute { Name = a.Name, Value = a.Value }).ToList(),
	};

	private static MushAttribute Attr(MushObject obj, string name) => obj.Attributes.First(a => a.Name == name);

	[Test]
	public async Task NoTabs_NothingActive()
	{
		var tabs = new SoftcodeTabs();

		tabs.Capture("ignored");

		await Assert.That(tabs.Count).IsEqualTo(0);
		await Assert.That(tabs.ActiveIndex).IsEqualTo(-1);
		await Assert.That(tabs.Active).IsNull();
	}

	[Test]
	public async Task Open_AddsAndActivates_AndReopeningActivatesTheExistingTab()
	{
		var obj = Obj(8, ("DESCRIBE", "A widget."), ("CMD", "$go:@pemit %#=hi"));
		var tabs = new SoftcodeTabs();

		var describe = tabs.Open(obj, Attr(obj, "DESCRIBE"));
		tabs.Open(obj, Attr(obj, "CMD"));
		var again = tabs.Open(obj, new MushAttribute { Name = "describe", Value = "different" });

		await Assert.That(tabs.Count).IsEqualTo(2);
		await Assert.That(again).IsSameReferenceAs(describe);
		await Assert.That(tabs.ActiveIndex).IsEqualTo(0);
		await Assert.That(describe.WorkingContent).IsEqualTo("A widget.");
		await Assert.That(describe.IsDirty).IsFalse();
	}

	[Test]
	public async Task SameAttributeName_OnAnotherObject_IsItsOwnTab()
	{
		var first = Obj(8, ("DESCRIBE", "eight"));
		var second = Obj(9, ("DESCRIBE", "nine"));
		var tabs = new SoftcodeTabs();

		tabs.Open(first, Attr(first, "DESCRIBE"));
		tabs.Open(second, Attr(second, "DESCRIBE"));

		await Assert.That(tabs.Count).IsEqualTo(2);
		await Assert.That(tabs.Active!.WorkingContent).IsEqualTo("nine");
	}

	[Test]
	public async Task Capture_IsDirtyByComparison_AndUndoingTheEditReadsClean()
	{
		var obj = Obj(8, ("DESCRIBE", "A widget."));
		var tabs = new SoftcodeTabs();
		var tab = tabs.Open(obj, Attr(obj, "DESCRIBE"));

		tabs.Capture("A gadget.");
		await Assert.That(tab.IsDirty).IsTrue();

		tabs.Capture("A widget.");
		await Assert.That(tab.IsDirty).IsFalse();
	}

	[Test]
	public async Task Switch_ToTheActiveTab_IsNoChange()
	{
		var obj = Obj(8, ("A", "a"), ("B", "b"));
		var tabs = new SoftcodeTabs();
		tabs.Open(obj, Attr(obj, "A"));
		tabs.Open(obj, Attr(obj, "B"));

		await Assert.That(tabs.Switch(1)).IsFalse();
		await Assert.That(tabs.Switch(0)).IsTrue();
		await Assert.That(tabs.Active!.Attr.Name).IsEqualTo("A");
	}

	[Test]
	public async Task Close_TheLastTab_LeavesNothingActive()
	{
		var obj = Obj(8, ("A", "a"));
		var tabs = new SoftcodeTabs();
		tabs.Open(obj, Attr(obj, "A"));

		var active = tabs.Close(0);

		await Assert.That(active).IsNull();
		await Assert.That(tabs.ActiveIndex).IsEqualTo(-1);
	}

	[Test]
	public async Task Close_TheActiveLastTab_ActivatesTheOneBeforeIt()
	{
		var obj = Obj(8, ("A", "a"), ("B", "b"), ("C", "c"));
		var tabs = new SoftcodeTabs();
		tabs.Open(obj, Attr(obj, "A"));
		tabs.Open(obj, Attr(obj, "B"));
		var c = tabs.Open(obj, Attr(obj, "C"));

		var active = tabs.Close(2);

		await Assert.That(active!.Attr.Name).IsEqualTo("B");
		await Assert.That(tabs.IndexOf(c)).IsEqualTo(-1);
	}

	[Test]
	public async Task Close_KeepsAnotherTabsUnsavedBuffer()
	{
		var obj = Obj(8, ("A", "a"), ("B", "b"));
		var tabs = new SoftcodeTabs();
		var a = tabs.Open(obj, Attr(obj, "A"));
		tabs.Capture("edited a");
		tabs.Open(obj, Attr(obj, "B"));

		var active = tabs.Close(1);

		await Assert.That(active).IsSameReferenceAs(a);
		await Assert.That(a.WorkingContent).IsEqualTo("edited a");
		await Assert.That(a.IsDirty).IsTrue();
	}

	[Test]
	public async Task MarkSaved_MakesTheBufferWhatTheDatabaseHolds()
	{
		var obj = Obj(8, ("A", "a"));
		var tabs = new SoftcodeTabs();
		var tab = tabs.Open(obj, Attr(obj, "A"));
		tabs.Capture("edited");

		tab.MarkSaved("edited");

		await Assert.That(tab.IsDirty).IsFalse();
		await Assert.That(tab.SavedContent).IsEqualTo("edited");
	}

	[Test]
	public async Task Refresh_FollowsTheReadOnCleanTabs_AndKeepsUnsavedEdits()
	{
		var obj = Obj(8, ("CLEAN", "old"), ("DIRTY", "old"), ("GONE", "kept"));
		var tabs = new SoftcodeTabs();
		var clean = tabs.Open(obj, Attr(obj, "CLEAN"));
		var dirty = tabs.Open(obj, Attr(obj, "DIRTY"));
		tabs.Capture("mine");
		var gone = tabs.Open(obj, Attr(obj, "GONE"));

		tabs.Refresh(Obj(8, ("clean", "theirs"), ("DIRTY", "theirs")));

		await Assert.That(clean.WorkingContent).IsEqualTo("theirs");
		await Assert.That(clean.IsDirty).IsFalse();
		await Assert.That(dirty.WorkingContent).IsEqualTo("mine");
		await Assert.That(dirty.SavedContent).IsEqualTo("theirs");
		await Assert.That(dirty.IsDirty).IsTrue();
		await Assert.That(gone.WorkingContent).IsEqualTo("kept");
	}

	[Test]
	public async Task Refresh_ReadingTheSameTextAsTheEdit_ReadsClean()
	{
		var obj = Obj(8, ("A", "old"));
		var tabs = new SoftcodeTabs();
		var tab = tabs.Open(obj, Attr(obj, "A"));
		tabs.Capture("new");

		tabs.Refresh(Obj(8, ("A", "new")));

		await Assert.That(tab.IsDirty).IsFalse();
	}

	[Test]
	public async Task Refresh_LeavesTabsOnOtherObjectsAlone()
	{
		var eight = Obj(8, ("A", "eight"));
		var tabs = new SoftcodeTabs();
		var tab = tabs.Open(eight, Attr(eight, "A"));

		tabs.Refresh(Obj(9, ("A", "nine")));

		await Assert.That(tab.SavedContent).IsEqualTo("eight");
	}
}
