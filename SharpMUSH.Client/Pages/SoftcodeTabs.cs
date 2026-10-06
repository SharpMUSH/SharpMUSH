using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Pages;

/// <summary>One attribute open in the Softcode Editor: what the editor buffer holds and what the database holds.</summary>
public sealed class SoftcodeTab(MushObject obj, MushAttribute attr)
{
	public MushObject Obj { get; } = obj;
	public MushAttribute Attr { get; } = attr;

	/// <summary>The editor buffer for this tab.</summary>
	/// <remarks>
	/// Verbatim: the stored value IS what the author typed. This used to translate every stored %r
	/// into a newline, which was the mirror of the save-side rewrite and made the two
	/// indistinguishable — an intentional %r looked like formatting and a real newline came back as
	/// %r on the next save.
	/// </remarks>
	public string WorkingContent { get; internal set; } = attr.Value;

	/// <summary>What the database holds.</summary>
	public string SavedContent { get; internal set; } = attr.Value;

	/// <summary>
	/// Whether the buffer differs from what the database holds — compared, never tracked by hand.
	/// </summary>
	/// <remarks>
	/// Monaco raises its content-changed event for a programmatic SetValue as well as for typing, so
	/// a hand-kept flag marked a tab unsaved on load, switch or refresh with no user input — and a
	/// "this write was mine" guard stays armed when SetValue throws or raises nothing, swallowing the
	/// user's next real edit. Comparing needs no assumption about when the event fires, and an edit
	/// undone back to the stored text reads clean again.
	/// </remarks>
	public bool IsDirty => !string.Equals(WorkingContent, SavedContent, StringComparison.Ordinal);

	/// <summary>
	/// <paramref name="value"/> is now what the database holds. The buffer is left alone: anything typed
	/// while the save was in flight is newer than <paramref name="value"/> and stays unsaved.
	/// </summary>
	public void MarkSaved(string value) => SavedContent = value;

	/// <summary>This tab's console: its scrollback and the arguments its runs pass.</summary>
	public SoftcodeConsole Repl { get; } = new();

	public bool Is(int dbref, string attributeName) =>
		Obj.Dbref == dbref && string.Equals(Attr.Name, attributeName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The Softcode Editor's open tabs: which attributes are open, which one is active, and which hold
/// unsaved edits.
/// </summary>
/// <remarks>
/// The page owns the Monaco editor and passes its text in: <see cref="Capture"/> before the active tab
/// changes, so the buffer it leaves is kept, and the active tab's <see cref="SoftcodeTab.WorkingContent"/>
/// is what the page loads into Monaco afterwards. Nothing here touches the editor, so every
/// transition is reachable without rendering.
/// </remarks>
public sealed class SoftcodeTabs
{
	private readonly List<SoftcodeTab> _tabs = [];

	public IReadOnlyList<SoftcodeTab> Tabs => _tabs;

	public int Count => _tabs.Count;

	public int ActiveIndex { get; private set; } = -1;

	public SoftcodeTab? Active => ActiveIndex >= 0 && ActiveIndex < _tabs.Count ? _tabs[ActiveIndex] : null;

	/// <summary>Records the editor's text as the active tab's buffer; does nothing with no tab active.</summary>
	public void Capture(string editorText)
	{
		if (Active is { } active)
		{
			active.WorkingContent = editorText;
		}
	}

	/// <summary>Activates the tab for <paramref name="attr"/> on <paramref name="obj"/>, opening it if it is not open yet.</summary>
	public SoftcodeTab Open(MushObject obj, MushAttribute attr)
	{
		var existing = _tabs.FindIndex(t => t.Is(obj.Dbref, attr.Name));
		if (existing >= 0)
		{
			ActiveIndex = existing;
		}
		else
		{
			_tabs.Add(new SoftcodeTab(obj, attr));
			ActiveIndex = _tabs.Count - 1;
		}

		return _tabs[ActiveIndex];
	}

	/// <summary>Makes the tab at <paramref name="index"/> active.</summary>
	/// <returns>Whether the active tab changed; activating the tab already active is no change.</returns>
	public bool Switch(int index)
	{
		if (index == ActiveIndex)
		{
			return false;
		}

		ActiveIndex = index;
		return true;
	}

	/// <summary>
	/// Closes the tab at <paramref name="index"/>. Closing another tab keeps the active one active; closing
	/// the active tab activates the one that takes its place, or the one before it at the end of the strip.
	/// </summary>
	/// <returns>The tab active afterwards, or <c>null</c> when none is left open.</returns>
	public SoftcodeTab? Close(int index)
	{
		_tabs.RemoveAt(index);
		if (index < ActiveIndex)
		{
			ActiveIndex--;
		}

		ActiveIndex = _tabs.Count == 0 ? -1 : Math.Clamp(ActiveIndex, 0, _tabs.Count - 1);
		return Active;
	}

	/// <summary>Where <paramref name="tab"/> sits in the strip, or -1 once it is closed.</summary>
	public int IndexOf(SoftcodeTab tab) => _tabs.IndexOf(tab);

	/// <summary>Re-syncs the tabs open on <paramref name="obj"/> with a fresh read of it.</summary>
	/// <remarks>
	/// The read is authoritative for what the database holds, unsaved edits or not; a clean tab's
	/// buffer follows it, and a dirty tab keeps its buffer. A tab whose buffer now matches what was
	/// read is clean, even if it was dirty a moment ago (someone else saving the same text, or our
	/// own save round-tripping). An attribute the read no longer has leaves its tab as it was.
	/// </remarks>
	public void Refresh(MushObject obj)
	{
		foreach (var tab in _tabs.Where(t => t.Obj.Dbref == obj.Dbref))
		{
			var refreshed = obj.Attributes.FirstOrDefault(a =>
				string.Equals(a.Name, tab.Attr.Name, StringComparison.OrdinalIgnoreCase));
			if (refreshed is null)
			{
				continue;
			}

			if (!tab.IsDirty)
			{
				tab.WorkingContent = refreshed.Value;
			}

			tab.SavedContent = refreshed.Value;
		}
	}
}
