using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>All three parts come from the adapter's runtime context, never posted actor fields.</summary>
public sealed record ApplicationDraftKey(string Application, string Actor, string Instance);

/// <summary>Optional in-memory application drafts. These are neither world records nor durable receipts.</summary>
public sealed class ApplicationDraftService(TimeProvider? timeProvider = null, IConnectionService? connections = null)
{
	private bool _subscribed;
	private readonly Dictionary<long, string> _terminalInstances = [];
	private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
	private readonly object _gate = new();
	private readonly Dictionary<ApplicationDraftKey, Entry> _entries = [];
	public const int MaximumInstances = 4096;
	public const int MaximumPayloadBytes = 65536;
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
	private sealed class Entry(DateTimeOffset now)
	{
		public SemaphoreSlim Lane { get; } = new(1, 1);
		public Dictionary<string, JsonElement> Fields { get; } = new(StringComparer.Ordinal);
		public DateTimeOffset Touched = now;
		public volatile bool Retired;
		public bool Reviewed;
		public string Operation = Guid.NewGuid().ToString("N");
		public string? Digest;
		public JsonElement? Receipt;
	}

	/// <summary>A binding-specific nonce: logout, disconnect and character rebind retire it.</summary>
	public string TerminalInstance(IConnectionService.ConnectionData connection)
	{
		lock (_gate)
		{
			if (!_subscribed && connections is not null)
			{
				_subscribed = true;
				connections.ListenState(change =>
				{
					lock (_gate)
					{
						if (_terminalInstances.Remove(change.Item1, out var retired)) RetireInstance(retired);
					}
				});
			}
			return _terminalInstances[connection.Handle] = $"terminal:{connection.Handle}:{connection.Binding.Id:N}";
		}
	}

	public bool CanReply(IConnectionService.ConnectionData original, string instance)
	{
		lock (_gate)
			return _terminalInstances.GetValueOrDefault(original.Handle) == instance
				&& connections?.Get(original.Handle) is { } current
				&& ReferenceEquals(current.Binding, original.Binding) && ReferenceEquals(current.Metadata, original.Metadata) && current.Ref == original.Ref
				&& current.State == IConnectionService.ConnectionState.LoggedIn;
	}

	public void RetireInstance(string instance)
	{
		lock (_gate)
			foreach (var (key, entry) in _entries.Where(pair => pair.Key.Instance == instance).ToArray())
			{
				entry.Retired = true;
				_entries.Remove(key);
			}
	}

	public async Task<JsonElement> RunAsync(ApplicationDraftKey key, string action, JsonElement declaration, JsonElement payload,
		Func<IReadOnlyDictionary<string, JsonElement>, Task<JsonElement>> validate,
		Func<IReadOnlyDictionary<string, JsonElement>, Task<JsonElement>> file, CancellationToken cancellationToken = default)
	{
		if (action is not ("new" or "set" or "review" or "back" or "file" or "cancel")) return Failure("Unknown draft action.");
		if (payload.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumPayloadBytes)
			return Failure("Draft fields must be a JSON object of at most 64 KiB.");
		if (!TryDeclaration(declaration, out var declarations, out var declarationError)) return Failure(declarationError);
		Entry entry;
		lock (_gate)
		{
			if (connections is not null && key.Instance.StartsWith("terminal:", StringComparison.Ordinal)
				&& !_terminalInstances.Values.Contains(key.Instance)) return Failure("This connection binding has retired.");
			foreach (var (expiredKey, expired) in _entries.Where(pair => _time.GetUtcNow() - pair.Value.Touched >= Lifetime).ToArray())
			{
				expired.Retired = true;
				_entries.Remove(expiredKey);
			}
			if (!_entries.TryGetValue(key, out entry!))
			{
				if (action is not ("new" or "review")) return Failure("No active draft. Start a new draft.");
				if (_entries.Count >= MaximumInstances || _entries.Count(pair => pair.Key.Actor == key.Actor) >= 64) return Failure("The application draft limit has been reached.");
				_entries[key] = entry = new(_time.GetUtcNow());
			}
		}
		await entry.Lane.WaitAsync(cancellationToken);
		try
		{
			if (entry.Retired) return Failure("This draft instance has retired.");
			lock (_gate) entry.Touched = _time.GetUtcNow();
			if (action == "cancel")
			{
				lock (_gate) { entry.Retired = true; _entries.Remove(key); }
				return JsonSerializer.SerializeToElement(new { ok = true, message = "Draft cancelled." });
			}
			if (action == "new")
			{
				entry.Fields.Clear(); entry.Reviewed = false; entry.Receipt = null; entry.Digest = null; entry.Operation = Guid.NewGuid().ToString("N");
			}
			var supplied = payload.EnumerateObject().Where(p => p.Name is not ("operation_id" or "op")).ToArray();
			if (supplied.Any(p => !declarations.ContainsKey(p.Name))) return Failure("Unknown draft field.");
			if (action == "file")
			{
				if (payload.TryGetProperty("operation_id", out var operation) && (operation.ValueKind != JsonValueKind.String || operation.GetString() != entry.Operation)) return Failure("The review operation has retired.");
				var submitted = new Dictionary<string, JsonElement>(entry.Fields, StringComparer.Ordinal);
				foreach (var p in supplied) submitted[p.Name] = p.Value.Clone();
				var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(submitted.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value)))));
				if (entry.Receipt is { } receipt) return digest == entry.Digest ? receipt : Failure("Conflicting reuse of the file operation.");
				if (!entry.Reviewed) return Failure("Review this draft before filing.");
				if (supplied.Any(p => !entry.Fields.TryGetValue(p.Name, out var value) || value.GetRawText() != p.Value.GetRawText())) return Failure("The fields changed after review. Review them again.");
				var errors = Validate(declarations, entry.Fields);
				var checkedResult = errors.Count > 0 ? FieldFailure(errors) : await validate(entry.Fields);
				if (entry.Retired) return Failure("This draft instance has retired.");
				entry.Digest = digest;
				entry.Receipt = Failure("The filing operation started but did not produce a receipt. Check the world before starting another draft.");
				var result = IsOk(checkedResult) ? await file(entry.Fields) : checkedResult;
				entry.Digest = digest;
				entry.Receipt = result.Clone();
				return result;
			}
			var candidate = new Dictionary<string, JsonElement>(entry.Fields, StringComparer.Ordinal);
			foreach (var p in supplied) candidate[p.Name] = p.Value.Clone();
			foreach (var (name, field) in declarations)
				if (!candidate.ContainsKey(name) && field.TryGetProperty("default", out var fallback)) candidate[name] = fallback.Clone();
			if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(candidate)) > MaximumPayloadBytes) return Failure("Draft values exceed 64 KiB.");
			entry.Fields.Clear();
			foreach (var (name, value) in candidate) entry.Fields[name] = value;
			if (supplied.Length > 0)
			{
				entry.Reviewed = false; entry.Receipt = null; entry.Digest = null; entry.Operation = Guid.NewGuid().ToString("N");
			}

			if (action == "review")
			{
				var errors = Validate(declarations, entry.Fields);
				if (errors.Count > 0) return FieldFailure(errors);
				var checkedResult = await validate(entry.Fields);
				if (!IsOk(checkedResult)) return checkedResult;
				if (entry.Retired) return Failure("This draft instance has retired.");
				if (entry.Receipt is not null) { entry.Receipt = null; entry.Digest = null; entry.Operation = Guid.NewGuid().ToString("N"); }
				entry.Reviewed = true;
			}
			if (action == "back") entry.Reviewed = false;
			return JsonSerializer.SerializeToElement(new { ok = true, fields = entry.Fields, phase = entry.Reviewed ? "review" : "edit", operation_id = entry.Operation });
		}
		finally { entry.Lane.Release(); }
	}

	private static bool Invalid(string message, out string error) { error = message; return false; }

	private static bool TryDeclaration(JsonElement declaration, out Dictionary<string, JsonElement> declarations, out string error)
	{
		declarations = new(StringComparer.Ordinal);
		error = "";
		if (declaration.ValueKind != JsonValueKind.Array || declaration.GetArrayLength() > 64) return Invalid("Draft declaration must contain at most 64 fields.", out error);
		foreach (var field in declaration.EnumerateArray())
		{
			if (field.ValueKind != JsonValueKind.Object || !field.TryGetProperty("key", out var name) || name.ValueKind != JsonValueKind.String
				|| string.IsNullOrWhiteSpace(name.GetString()) || !declarations.TryAdd(name.GetString()!, field)) return Invalid("Draft field keys must be unique nonempty strings.", out error);
			if (field.TryGetProperty("type", out var t) && t.ValueKind != JsonValueKind.String) return Invalid("Draft field type must be a string.", out error);
			var type = t.ValueKind == JsonValueKind.String ? t.GetString() : "text";
			if (field.TryGetProperty("validation", out var rules))
			{
				if (rules.ValueKind != JsonValueKind.Object || rules.EnumerateObject().Any(rule => rule.Name is not ("required" or "max_length"))) return Invalid("Draft validation supports required and max_length only.", out error);
				if (rules.TryGetProperty("required", out var required) && required.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Invalid("required must be a boolean.", out error);
				if (rules.TryGetProperty("max_length", out var length) && (length.ValueKind != JsonValueKind.Number || !length.TryGetInt32(out var limit) || limit < 0)) return Invalid("max_length must be a nonnegative integer.", out error);
			}
			if (type is not ("text" or "textarea" or "select" or "boolean" or "number" or "date" or "list" or "multiselect")) return Invalid("Unsupported draft field type.", out error);
		}
		if (declarations.Keys.Any(key => key is "op" or "operation_id")) return Invalid("op and operation_id are reserved transport fields.", out error);
		return true;
	}

	/// <summary>Pure shared validation for legacy entry points that do not retain a draft.</summary>
	public static JsonElement ValidateFields(JsonElement declaration, JsonElement fields)
	{
		if (!TryDeclaration(declaration, out var definitions, out var error)) return Failure(error);
		if (fields.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(fields.GetRawText()) > MaximumPayloadBytes) return Failure("Expected a fields object of at most 64 KiB.");
		try
		{
			var values = fields.EnumerateObject().ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
			if (values.Keys.Any(key => !definitions.ContainsKey(key))) return Failure("Unknown application field.");
			foreach (var (key, field) in definitions)
				if (!values.ContainsKey(key) && field.TryGetProperty("default", out var fallback)) values[key] = fallback;
			var errors = Validate(definitions, values);
			return errors.Count == 0 ? JsonSerializer.SerializeToElement(new { ok = true }) : FieldFailure(errors);
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException) { return Failure("Invalid field declaration."); }
	}

	public static bool IsOk(JsonElement result) => result.ValueKind == JsonValueKind.Object && result.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
	public static JsonElement Failure(string message) => FieldFailure(new Dictionary<string, string> { ["_global"] = message });
	private static JsonElement FieldFailure(IReadOnlyDictionary<string, string> errors) => JsonSerializer.SerializeToElement(new { ok = false, errors });

	/// <summary>Structural checks shared by the terminal and HTTP adapter; world checks stay in callbacks.</summary>
	private static Dictionary<string, string> Validate(IReadOnlyDictionary<string, JsonElement> declarations, IReadOnlyDictionary<string, JsonElement> fields)
	{
		var errors = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (key, declaration) in declarations)
		{
			var present = fields.TryGetValue(key, out var value);
			var text = present && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
			if (declaration.TryGetProperty("validation", out var validation))
			{
				if (validation.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True
					&& (!present || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0 || text is not null && string.IsNullOrWhiteSpace(text))) errors[key] = $"{key} is required.";
				if (text is not null && validation.TryGetProperty("max_length", out var max) && max.TryGetInt32(out var limit) && text.Length > limit) errors[key] = $"{key} must be at most {limit} characters.";
			}
			if (!present || value.ValueKind == JsonValueKind.Null) continue;
			var type = declaration.TryGetProperty("type", out var t) ? t.GetString() : "text";
			var valid = type switch
			{
				"boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
				"number" => value.ValueKind == JsonValueKind.Number,
				"date" => text is not null && DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _),
				"list" or "multiselect" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String),
				_ => value.ValueKind == JsonValueKind.String
			};
			if (!valid) errors[key] = $"{key} has the wrong JSON type.";
		}
		return errors;
	}
}
