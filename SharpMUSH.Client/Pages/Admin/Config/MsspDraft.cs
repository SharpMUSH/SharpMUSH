using SharpMUSH.Configuration.Mssp;

namespace SharpMUSH.Client.Pages.Admin.Config;

/// <summary>A variable MSSP does not list, as the "Other variables" card edits it.</summary>
public sealed class MsspOtherVariable
{
	public string Name { get; set; } = string.Empty;

	public List<string> Values { get; } = [];

	/// <summary>The value being typed, added on Enter.</summary>
	public string ValueDraft { get; set; } = string.Empty;
}

/// <summary>
/// The MSSP page's unsaved edits: the catalog's settable variables by name, and the other variables
/// in the order they were added. <see cref="Settings"/> is what a save sends.
/// </summary>
public sealed class MsspDraft
{
	private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _listDrafts = new(StringComparer.Ordinal);
	private IReadOnlyDictionary<string, string[]> _saved = new Dictionary<string, string[]>();

	public List<MsspOtherVariable> Others { get; } = [];

	/// <summary>Starts over from what the server has saved.</summary>
	public void Load(IReadOnlyDictionary<string, string[]> saved)
	{
		_saved = new Dictionary<string, string[]>(saved, StringComparer.Ordinal);
		_values.Clear();
		_listDrafts.Clear();
		Others.Clear();

		foreach (var (name, values) in saved)
		{
			if (MsspCatalog.Find(name) is { } variable)
			{
				_values[variable.Name] = [.. values];
				continue;
			}

			var other = new MsspOtherVariable { Name = name };
			other.Values.AddRange(values);
			Others.Add(other);
		}
	}

	public IReadOnlyList<string> Values(string name) => _values.TryGetValue(name, out var values) ? values : [];

	public string Single(string name) => Values(name) is [.., var last] ? last : string.Empty;

	public void SetSingle(string name, string? value)
	{
		var trimmed = value?.Trim() ?? string.Empty;
		_values[name] = trimmed.Length == 0 ? [] : [trimmed];
	}

	/// <summary>Picks <paramref name="value"/> for a one-value variable, or clears it when it was already picked.</summary>
	public void Toggle(string name, string value) =>
		SetSingle(name, Single(name) == value ? null : value);

	/// <summary>Adds or removes <paramref name="value"/> among a many-value variable's values.</summary>
	public void ToggleMany(string name, string value)
	{
		var values = Mutable(name);
		if (!values.Remove(value))
		{
			values.Add(value);
		}
	}

	public string ListDraft(string name) => _listDrafts.GetValueOrDefault(name, string.Empty);

	public void SetListDraft(string name, string text) => _listDrafts[name] = text;

	/// <summary>Adds the typed value to a list variable, unless it is blank or already there.</summary>
	public void CommitListDraft(string name)
	{
		var text = ListDraft(name).Trim();
		_listDrafts[name] = string.Empty;
		var values = Mutable(name);
		if (text.Length > 0 && !values.Contains(text))
		{
			values.Add(text);
		}
	}

	public void RemoveAt(string name, int index)
	{
		var values = Mutable(name);
		if (index >= 0 && index < values.Count)
		{
			values.RemoveAt(index);
		}
	}

	public void CommitOtherDraft(MsspOtherVariable other)
	{
		var text = other.ValueDraft.Trim();
		other.ValueDraft = string.Empty;
		if (text.Length > 0)
		{
			other.Values.Add(text);
		}
	}

	/// <summary>What a save sends: every variable with a value, the catalog's first, in its order.</summary>
	public Dictionary<string, string[]> Settings()
	{
		var settings = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (var variable in MsspCatalog.All.Where(variable => !variable.ReportedByServer))
		{
			if (Values(variable.Name) is { Count: > 0 } values)
			{
				settings[variable.Name] = [.. values];
			}
		}

		foreach (var other in Others.Where(other => other.Name.Trim().Length > 0 || other.Values.Count > 0))
		{
			// Two rows under one name are reported, not merged: a save must send what the page shows.
			settings.TryAdd(other.Name.Trim(), [.. other.Values]);
		}

		return settings;
	}

	/// <summary>Why each variable cannot be saved, by the name its row edits. Empty when the draft can be saved.</summary>
	public Dictionary<string, string> Problems(Func<string, string> hasField)
	{
		var problems = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var variable in MsspCatalog.All.Where(variable => !variable.ReportedByServer))
		{
			if (Values(variable.Name) is { Count: > 0 } values && MsspCatalog.Validate(variable.Name, values) is { } problem)
			{
				problems[variable.Name] = problem;
			}
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var other in Others)
		{
			var key = OtherKey(other);
			var canonical = MsspCatalog.Canonicalize(other.Name);
			if (canonical.Length == 0 && other.Values.Count == 0)
			{
				continue;
			}

			if (MsspCatalog.Find(canonical) is { ReportedByServer: false } catalogued)
			{
				problems[key] = hasField(catalogued.Name);
			}
			else if (!seen.Add(canonical))
			{
				problems[key] = $"{canonical} is given twice.";
			}
			else if (MsspCatalog.Validate(canonical, other.Values) is { } problem)
			{
				problems[key] = problem;
			}
		}

		return problems;
	}

	/// <summary>The key an other variable's problem is filed under: its row, since its name may be the problem.</summary>
	public string OtherKey(MsspOtherVariable other) => $"other:{Others.IndexOf(other)}";

	/// <summary>True when the catalog variable <paramref name="name"/> differs from what is saved.</summary>
	public bool IsChanged(string name) =>
		!Values(name).SequenceEqual(_saved.TryGetValue(name, out var saved) ? saved : []);

	/// <summary>How many variables a save would change, add or remove.</summary>
	public int ChangedCount
	{
		get
		{
			var now = Settings().ToDictionary(entry => MsspCatalog.Canonicalize(entry.Key), entry => entry.Value);
			return now.Keys.Union(_saved.Keys)
				.Count(name => !(now.TryGetValue(name, out var a) ? a : []).SequenceEqual(_saved.TryGetValue(name, out var b) ? b : []));
		}
	}

	public bool HasChanges => ChangedCount > 0;

	private List<string> Mutable(string name)
	{
		if (!_values.TryGetValue(name, out var values))
		{
			values = [];
			_values[name] = values;
		}

		return values;
	}
}
