using SharpMUSH.Client.Models.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Pages.Admin.Config;

/// <summary>
/// The working copy behind <c>/admin/config/{category}</c>: the values as loaded, the values as
/// edited, what the operator has typed but not committed, and the server's refusals per field.
/// </summary>
/// <remarks>
/// <para>Every edit the page offers is a method here, and each one leaves <see cref="ChangedCount"/>
/// agreeing with <see cref="Changes"/>, which is what the save sends. The page renders this state and
/// calls these methods; it holds no editing state of its own, so all of it is reachable without
/// rendering.</para>
///
/// <para>A dictionary-valued property is edited through a draft list (<see cref="DictEntries"/>) that
/// keeps entry order and tolerates a blank or repeated key halfway through an edit; every change to
/// it is written back to the value as a dictionary at once.</para>
/// </remarks>
public sealed class ConfigDraft
{
	private readonly Dictionary<string, object?> _current = [];
	private readonly Dictionary<string, object?> _original = [];
	private readonly Dictionary<string, string> _errors = [];
	private readonly Dictionary<string, string> _listDrafts = [];
	private readonly Dictionary<string, List<ConfigDictEntry>> _dictDrafts = [];

	/// <summary>How many properties differ from the values as loaded.</summary>
	public int ChangedCount { get; private set; }

	public bool HasChanges => ChangedCount > 0;

	/// <summary>The edited values, keyed by property path.</summary>
	public IReadOnlyDictionary<string, object?> Values => _current;

	/// <summary>Starts over from <paramref name="configuration"/>, for <paramref name="properties"/>.</summary>
	/// <remarks>
	/// Whatever was held before is dropped, edits included: a draft is one category's, and an edit left
	/// behind in another category must not ride along with this one's save.
	/// </remarks>
	public void Load(IEnumerable<PropertyMetadata> properties, SharpMUSHOptions configuration)
	{
		Clear();

		foreach (var property in properties)
		{
			var value = ConfigAccessor.GetValue(configuration, property.Name);
			_current[property.Path] = value;
			_original[property.Path] = value;
		}

		Recalculate();
	}

	/// <summary>Holds nothing: no values, no edits, nothing typed and no refusals.</summary>
	public void Clear()
	{
		_current.Clear();
		_original.Clear();
		ClearEditing();
		ChangedCount = 0;
	}

	/// <summary>The properties whose value differs from the one loaded; what a save sends.</summary>
	public Dictionary<string, object?> Changes() => ConfigValues.Changes(_current, _original);

	public bool IsChanged(string path) =>
		_current.TryGetValue(path, out var current)
		&& _original.TryGetValue(path, out var original)
		&& !Equals(current, original);

	public bool Bool(PropertyMetadata property) => ConfigValues.Bool(_current, property);

	public string String(PropertyMetadata property) => ConfigValues.String(_current, property);

	public string NumericString(PropertyMetadata property) => ConfigValues.NumericString(_current, property);

	public List<string> StringList(PropertyMetadata property) => ConfigValues.StringList(_current, property);

	/// <summary>The server's refusal for this property, from the last save.</summary>
	public bool TryGetError(string path, out string? error) => _errors.TryGetValue(path, out error);

	/// <summary>Sets a value and clears the refusal standing against it.</summary>
	public void Set(PropertyMetadata property, object? value)
	{
		_current[property.Path] = value;
		_errors.Remove(property.Path);
		Recalculate();
	}

	/// <summary>Sets a number from what was typed — only when it parses, so a mid-edit "-" leaves it alone.</summary>
	public void SetNumeric(PropertyMetadata property, string? raw)
	{
		if (ConfigValues.ParseNumeric(property, raw, out var value))
			Set(property, value);
	}

	public string ListDraft(PropertyMetadata property) => _listDrafts.GetValueOrDefault(property.Path, string.Empty);

	public void SetListDraft(PropertyMetadata property, string text) => _listDrafts[property.Path] = text;

	/// <summary>Adds what was typed to the list, unless it is blank or already there; the box empties either way.</summary>
	public void CommitListDraft(PropertyMetadata property)
	{
		var draft = ListDraft(property).Trim();
		_listDrafts[property.Path] = string.Empty;
		if (draft.Length == 0) return;

		var list = StringList(property);
		if (list.Contains(draft)) return;

		list.Add(draft);
		Set(property, list.ToArray());
	}

	public void RemoveListItemAt(PropertyMetadata property, int index)
	{
		var list = StringList(property);
		if (index >= 0 && index < list.Count)
			list.RemoveAt(index);
		Set(property, list.ToArray());
	}

	/// <summary>The entries a dictionary-valued property is being edited as, built from its value on first use.</summary>
	public List<ConfigDictEntry> DictEntries(PropertyMetadata property)
	{
		if (!_dictDrafts.TryGetValue(property.Path, out var draft))
		{
			draft = ConfigValues.BuildDictEntries(_current.GetValueOrDefault(property.Path));
			_dictDrafts[property.Path] = draft;
		}

		return draft;
	}

	public void SetDictKey(PropertyMetadata property, int entryIndex, string key)
	{
		var draft = DictEntries(property);
		if (entryIndex >= 0 && entryIndex < draft.Count)
			draft[entryIndex].Key = key;
		SyncDict(property);
	}

	public void AddDictEntry(PropertyMetadata property)
	{
		DictEntries(property).Add(new ConfigDictEntry());
		SyncDict(property);
	}

	public void RemoveDictEntry(PropertyMetadata property, int entryIndex)
	{
		var draft = DictEntries(property);
		if (entryIndex >= 0 && entryIndex < draft.Count)
			draft.RemoveAt(entryIndex);
		SyncDict(property);
	}

	/// <summary>
	/// Adds what was typed into an entry's value box to that entry, unless it is blank or already
	/// there; the box empties either way.
	/// </summary>
	public void CommitDictValueDraft(PropertyMetadata property, int entryIndex)
	{
		var draft = DictEntries(property);
		if (entryIndex < 0 || entryIndex >= draft.Count) return;

		var entry = draft[entryIndex];
		var value = entry.ValueDraft.Trim();
		entry.ValueDraft = string.Empty;
		if (value.Length == 0 || entry.Values.Contains(value)) return;

		entry.Values.Add(value);
		SyncDict(property);
	}

	public void RemoveDictValueAt(PropertyMetadata property, int entryIndex, int valueIndex)
	{
		var draft = DictEntries(property);
		if (entryIndex >= 0 && entryIndex < draft.Count &&
				valueIndex >= 0 && valueIndex < draft[entryIndex].Values.Count)
			draft[entryIndex].Values.RemoveAt(valueIndex);
		SyncDict(property);
	}

	/// <summary>Puts every value back as loaded, and drops what was typed and every refusal.</summary>
	public void ResetChanges()
	{
		foreach (var (path, value) in _original)
			_current[path] = value;

		ClearEditing();
		ChangedCount = 0;
	}

	/// <summary>Sets every one of <paramref name="properties"/> to its shipped default, as an edit to be saved.</summary>
	public void ResetToDefaults(IEnumerable<PropertyMetadata> properties)
	{
		foreach (var property in properties)
			_current[property.Path] = ConfigValues.DefaultOf(property);

		_dictDrafts.Clear();
		_listDrafts.Clear();
		Recalculate();
	}

	/// <summary>Records that <paramref name="saved"/> reached the server: those values are now the loaded ones.</summary>
	public void MarkSaved(IEnumerable<string> saved)
	{
		foreach (var path in saved)
			_original[path] = _current[path];

		Recalculate();
	}

	/// <summary>Forgets the refusals from the last save, as a new save starts.</summary>
	public void ClearErrors() => _errors.Clear();

	/// <summary>Places each of the server's per-field refusals next to its field.</summary>
	/// <param name="fallback">What to show for a refusal that names a field but gives no reason.</param>
	public void SetErrors(IReadOnlyDictionary<string, string?> errors, string fallback)
	{
		foreach (var (path, error) in errors)
			_errors[path] = error ?? fallback;
	}

	private void SyncDict(PropertyMetadata property) =>
		Set(property, ConfigValues.ToDictionary(_dictDrafts[property.Path]));

	private void ClearEditing()
	{
		_errors.Clear();
		_dictDrafts.Clear();
		_listDrafts.Clear();
	}

	private void Recalculate() => ChangedCount = Changes().Count;
}
