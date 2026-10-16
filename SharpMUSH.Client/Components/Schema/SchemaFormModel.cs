using System.Text.Json;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// The state behind <see cref="SchemaFormRenderer"/>: the document being filled in, which page is
/// showing, the values entered, and the errors bound to fields or to the form as a whole.
/// </summary>
/// <remarks>
/// <para>The renderer draws this state and sends its values; it holds none of its own beyond whether a
/// request is in flight, so every edit, page move, validation and error binding is reachable without
/// rendering.</para>
///
/// <para>Values outlive a document: when an action answers with a replacement document, what was
/// entered under a key the new one also has is kept, and only keys with nothing entered are seeded
/// from the prefill data or the field's default.</para>
/// </remarks>
public sealed class SchemaFormModel
{
	private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

	public PortalSchemaDocument? Document { get; private set; }

	/// <summary>Local draft values, including retained keys omitted from the current payload.</summary>
	public IReadOnlyDictionary<string, object?> Values => _values;

	/// <summary>The error for the form as a whole, shown above it; <c>null</c> when there is none.</summary>
	public string? GlobalError { get; set; }

	public int PageIndex { get; private set; }

	public int PageCount => Document?.Pages?.Count ?? 0;

	public bool HasPages => PageCount > 0;

	/// <summary>The page showing, by the pages' <c>Order</c>.</summary>
	public SchemaPage CurrentPage => Document!.Pages!.OrderBy(p => p.Order).ElementAt(PageIndex);

	public bool IsFirstPage => PageIndex == 0;

	public bool IsLastPage => PageIndex >= PageCount - 1;

	/// <summary>
	/// Shows <paramref name="document"/> from its first page, seeding each field with nothing entered
	/// from <paramref name="data"/>, else from the field's default.
	/// </summary>
	public void Load(PortalSchemaDocument? document, SchemaData? data)
	{
		Document = document;
		PageIndex = 0;
		_errors.Clear();

		foreach (var field in Fields())
		{
			var key = field.Key!;
			if (_values.ContainsKey(key))
			{
				continue;
			}

			if (data?.Fields is not null && data.Fields.TryGetValue(key, out var datum))
			{
				_values[key] = datum.Value is { } value ? Seed(value) : null;
			}
			else if (field.Default is { } fallback)
			{
				_values[key] = Seed(fallback);
			}
		}
	}

	/// <summary>Every input field the document declares, on every page.</summary>
	public IEnumerable<SchemaElement> Fields()
		=> (Document?.Pages ?? [])
			.SelectMany(p => p.Sections ?? [])
			.SelectMany(s => s.Elements ?? [])
			.Where(e => string.Equals(e.Kind ?? "field", "field", StringComparison.OrdinalIgnoreCase) && e.Key is not null);

	/// <summary>Editable controls that can visibly bind a field error.</summary>
	public IEnumerable<SchemaElement> VisibleFields() => (HasPages ? CurrentPage.Sections ?? [] : [])
		.SelectMany(section => section.Elements ?? [])
		.Where(field => string.Equals(field.Kind ?? "field", "field", StringComparison.OrdinalIgnoreCase)
			&& field.Key is not null && !string.Equals(field.Type, "hidden", StringComparison.OrdinalIgnoreCase));

	/// <summary>Returned data is authoritative for every supplied key, including explicit null.</summary>
	public void ApplyData(SchemaData? data)
	{
		foreach (var (key, datum) in data?.Fields ?? new Dictionary<string, SchemaFieldValue>())
			_values[key] = datum.Value is { } value ? Seed(value) : null;
	}

	public bool HasAction(string name) => Document?.Actions?.ContainsKey(name) == true;

	/// <summary>The action named <paramref name="name"/>, when the document declares it with a route to send to.</summary>
	public SchemaAction? Action(string name)
		=> Document?.Actions is { } actions && actions.TryGetValue(name, out var action) && !string.IsNullOrWhiteSpace(action.Route)
			? action
			: null;

	public void NextPage()
	{
		if (PageIndex < PageCount - 1)
		{
			PageIndex++;
		}
	}

	public void PrevPage()
	{
		if (PageIndex > 0)
		{
			PageIndex--;
		}
	}

	public string GetString(string key) => _values.TryGetValue(key, out var v)
		? v switch
		{
			IEnumerable<string> list => string.Join(", ", list),
			_ => v?.ToString() ?? ""
		}
		: "";

	public void SetString(string key, string? value) => Set(key, value);

	public bool GetBool(string key) => _values.TryGetValue(key, out var v) && v is true;

	public void SetBool(string key, bool value) => Set(key, value);

	public double? GetNumber(string key)
		=> _values.TryGetValue(key, out var v) && v is not null
			? v is JsonElement { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out var value) ? value
				: double.TryParse(v.ToString(), out var d) ? d : null
			: null;

	public void SetNumber(string key, double? value) => Set(key, value);

	public IEnumerable<string> GetMulti(string key)
		=> _values.TryGetValue(key, out var v) && v is IEnumerable<string> list ? list : [];

	public void SetMulti(string key, IEnumerable<string> values) => Set(key, values.ToList());

	/// <summary>The date a field holds, or <c>null</c> when it holds none that parses.</summary>
	public DateTime? GetDate(string key) => DateTime.TryParse(GetString(key), out var d) ? d : null;

	/// <summary>Stores a date as <c>yyyy-MM-dd</c>, the form softcode receives it in.</summary>
	public void SetDate(string key, DateTime? date) => SetString(key, date?.ToString("yyyy-MM-dd"));

	/// <summary>The error bound to <paramref name="key"/>, or <c>null</c>.</summary>
	public string? Error(string key) => _errors.TryGetValue(key, out var e) ? e : null;

	/// <summary>Clears every error ahead of sending an action.</summary>
	public void ClearErrors()
	{
		GlobalError = null;
		_errors.Clear();
	}

	/// <summary>
	/// The advisory check before a final submit: every required field holds something. Softcode
	/// remains the authority; this only spares a round trip.
	/// </summary>
	/// <param name="requiredMessage">The error for one empty required field.</param>
	/// <param name="summary">The form's error when any required field is empty.</param>
	/// <returns>Whether every required field is filled in.</returns>
	public bool ValidateRequired(Func<SchemaElement, string> requiredMessage, string summary)
	{
		ClearErrors();
		foreach (var field in Fields().Where(f => f.Validation?.Required == true && string.IsNullOrWhiteSpace(GetString(f.Key!))))
		{
			_errors[field.Key!] = requiredMessage(field);
		}

		if (_errors.Count == 0)
		{
			return true;
		}

		GlobalError = summary;
		return false;
	}

	/// <summary>
	/// Binds a failed action's errors: <c>_global</c> to the form, any other key to its field — or
	/// every one to the form when <paramref name="bindToFields"/> is off.
	/// </summary>
	/// <param name="noErrors">The form's error when the action failed without saying why.</param>
	public void BindErrors(IReadOnlyDictionary<string, string>? errors, bool bindToFields, string noErrors)
	{
		if (errors is null || errors.Count == 0)
		{
			GlobalError = noErrors;
			return;
		}

		foreach (var (key, message) in errors)
		{
			if (key == "_global" || !bindToFields || Document?.IsForm != true || !VisibleFields().Any(f => f.Key == key))
			{
				GlobalError = GlobalError is null ? message : $"{GlobalError}\n{message}";
			}
			else
			{
				_errors[key] = message;
			}
		}
	}

	/// <summary>
	/// What an action posts: the values entered, with <paramref name="overrides"/> (a button's or timeline
	/// entry's <c>values</c>) merged over them. The entered values themselves are left as they are.
	/// </summary>
	public IReadOnlyDictionary<string, object?> Payload(IReadOnlyDictionary<string, JsonElement>? overrides)
	{
		var active = (Document?.IsForm == true ? Fields() : []).Select(f => f.Key!).ToHashSet(StringComparer.Ordinal);
		var payload = _values.Where(pair => active.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		if (overrides is null) return payload;

		foreach (var (key, value) in overrides)
		{
			payload[key] = value;
		}

		return payload;
	}

	/// <summary>Clears every value entered, ahead of merging an action's answer (<c>reset_fields</c>).</summary>
	public void ResetValues() => _values.Clear();

	public void ResetValues(SchemaFieldReset reset)
	{
		if (reset.All) _values.Clear();
		else foreach (var key in reset.Keys ?? []) _values.Remove(key);
	}

	/// <summary>Takes the values a successful action answered with.</summary>
	public void MergeFields(IReadOnlyDictionary<string, JsonElement> fields)
	{
		foreach (var (key, value) in fields)
		{
			_values[key] = Seed(value);
		}
	}

	/// <summary>A value as the form holds it: a JSON list as its items (multiselect, list), a JSON
	/// boolean as a bool; numbers, objects and mixed arrays retain their JSON types.</summary>
	private static object? Seed(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.Array when value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) => value.EnumerateArray().Select(item => item.GetString()!).ToList(),
		JsonValueKind.Array => value.Clone(),
		JsonValueKind.Number => value.Clone(),
		JsonValueKind.Null or JsonValueKind.Undefined => null,
		JsonValueKind.Object => value.Clone(),
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		_ => SchemaViewRenderer.ValueToString(value)
	};

	/// <summary>The width, out of 12, of one element in an N-column section, honouring its span.</summary>
	public static int ColumnWidth(int columns, int span)
		=> Math.Clamp(12 / Math.Max(1, columns) * Math.Max(1, span), 1, 12);

	private void Set(string key, object? value)
	{
		_values[key] = value;
		_errors.Remove(key);
	}
}
