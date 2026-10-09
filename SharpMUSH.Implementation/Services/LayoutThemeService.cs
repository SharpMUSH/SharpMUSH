using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>The layout themes staff added and the built-in ones they disabled (expanded server data).</summary>
public sealed class LayoutThemesData
{
	/// <summary>Each added theme's JSON, by its lower-case name.</summary>
	public Dictionary<string, string> Added { get; set; } = [];

	/// <summary>The built-in themes turned off, lower case.</summary>
	public List<string> Disabled { get; set; } = [];
}

/// <inheritdoc cref="ILayoutThemeService"/>
public partial class LayoutThemeService(IExpandedObjectDataService data) : ILayoutThemeService
{
	/// <summary>The longest name a theme can be added under.</summary>
	public const int MaxNameLength = 32;

	/// <summary>What naming a theme that cannot be added under that name answers.</summary>
	public const string BadName = "#-1 THEME NAMES ARE LETTERS, DIGITS AND HYPHENS";

	/// <summary>What adding a theme under a built-in one's name answers.</summary>
	public const string BuiltInName = "#-1 THAT IS A BUILT-IN THEME";

	private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	private sealed record Snapshot(ImmutableDictionary<string, string> Added, ImmutableHashSet<string> Disabled)
	{
		public static Snapshot Empty { get; } = new(ImmutableDictionary<string, string>.Empty, ImmutableHashSet<string>.Empty);
	}

	// One record holds every theme: two changes at once must not each drop the other's.
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private volatile Snapshot _themes = Snapshot.Empty;

	/// <inheritdoc />
	public Result<ThemePalette> Read(string spec) => Resolve(spec) switch
	{
		string resolved => LayoutThemes.Read(resolved),
		Error<string> error => error,
	};

	/// <inheritdoc />
	public Result<string> Resolve(string spec) => Resolve(spec, _themes, allowDisabled: false);

	/// <inheritdoc />
	public IEnumerable<string> Names
	{
		get
		{
			var themes = _themes;
			return LayoutThemes.Names.Where(name => !themes.Disabled.Contains(name.ToLowerInvariant()))
				.Concat(themes.Added.Keys.Order(StringComparer.Ordinal));
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<LayoutThemeEntry> List()
	{
		var themes = _themes;
		return
		[
			.. LayoutThemes.Names.Select(name => new LayoutThemeEntry(name, true, themes.Disabled.Contains(name.ToLowerInvariant()), null)),
			.. themes.Added.OrderBy(added => added.Key, StringComparer.Ordinal).Select(added => new LayoutThemeEntry(added.Key, false, false, added.Value)),
		];
	}

	/// <inheritdoc />
	public async ValueTask LoadAsync() => _themes = Of(await data.GetExpandedServerDataAsync<LayoutThemesData>() ?? new LayoutThemesData());

	/// <inheritdoc />
	public async ValueTask<Result<LayoutThemeEntry>> AddAsync(string name, string definition)
	{
		name = name.Trim().ToLowerInvariant();
		if (!ThemeName().IsMatch(name)) return new Error<string>(BadName);
		if (LayoutThemes.IsBuiltIn(name) || name == LayoutThemes.None) return new Error<string>(BuiltInName);

		Result<LayoutThemeEntry> result = new Error<string>(LayoutThemes.Unknown);
		await ChangeAsync(stored =>
		{
			// A disabled theme is still one an added theme can start from: disabling it only takes it off the list.
			result = Resolve(definition, Of(stored), allowDisabled: true) switch
			{
				string resolved => LayoutThemes.Read(resolved) switch
				{
					ThemePalette => new LayoutThemeEntry(name, false, false, Written(resolved, name)),
					Error<string> unreadable => unreadable,
				},
				Error<string> unresolved => unresolved,
			};
			if (result is not LayoutThemeEntry entry) return false;
			stored.Added[name] = entry.Definition!;
			return true;
		});
		return result;
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<Success>> RemoveAsync(string name)
	{
		name = name.Trim().ToLowerInvariant();
		FoundResult<Success> result = new NotFound();
		await ChangeAsync(stored =>
		{
			if (!stored.Added.Remove(name)) return false;
			result = new Success();
			return true;
		});
		return result;
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<Success>> SetDisabledAsync(string name, bool disabled)
	{
		name = name.Trim().ToLowerInvariant();
		if (!LayoutThemes.IsBuiltIn(name))
		{
			return _themes.Added.ContainsKey(name)
				? new Error<string>("#-1 ONLY A BUILT-IN THEME IS DISABLED; REMOVE AN ADDED ONE")
				: new NotFound();
		}

		await ChangeAsync(stored =>
		{
			var changed = disabled ? !stored.Disabled.Contains(name) : stored.Disabled.Remove(name);
			if (disabled && changed) stored.Disabled.Add(name);
			return changed;
		});
		return new Success();
	}

	/// <summary>
	/// What a name in a theme stands for: an added theme's or one of SharpMUSH's presets' JSON, an error for a disabled
	/// one, or nothing to change.
	/// </summary>
	private union Naming(JsonNode, Error<string>, None);

	/// <summary>
	/// <paramref name="spec"/> with each added theme it names written out in its place. A bare word, a JSON string
	/// and a <c>"preset"</c> (at any depth) name a theme.
	/// </summary>
	private static Result<string> Resolve(string spec, Snapshot themes, bool allowDisabled)
	{
		spec = spec.Trim();
		try
		{
			JsonNode? node;
			try
			{
				node = JsonNode.Parse(spec);
			}
			catch (JsonException)
			{
				// Not JSON: a bare word names a theme; anything else is left for LayoutThemes.Read to refuse.
				return Substituted(Named(spec, themes, allowDisabled), spec);
			}

			return node switch
			{
				JsonValue value when value.TryGetValue<string>(out var name) => Substituted(Named(name, themes, allowDisabled), spec),
				JsonObject theme => Rewrite(theme, themes, allowDisabled) switch
				{
					true => theme.ToJsonString(Compact),
					false => spec,
					Error<string> error => error,
				},
				_ => spec,
			};
		}
		catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
		{
			// Text JSON cannot hold (half a surrogate pair): LayoutThemes.Read says why.
			return spec;
		}
	}

	private static Result<string> Substituted(Naming naming, string spec) => naming switch
	{
		JsonNode added => added.ToJsonString(Compact),
		Error<string> error => error,
		None => spec,
	};

	private static Naming Named(string name, Snapshot themes, bool allowDisabled)
	{
		var key = name.Trim().ToLowerInvariant();
		if (themes.Added.TryGetValue(key, out var definition)) return JsonNode.Parse(definition)!;
		if (!allowDisabled && themes.Disabled.Contains(key)) return new Error<string>(LayoutThemes.Unknown);
		// SharpMUSH's own presets are written out too: a connection's renderer knows only MarkupString's.
		return LayoutThemes.OwnPreset(key) is { } own ? JsonNode.Parse(own.ToJson())! : new None();
	}

	/// <summary>
	/// Writes out the added themes <paramref name="theme"/>'s <c>"preset"</c> names, at any depth: whether it
	/// changed anything, or the error for a disabled one.
	/// </summary>
	private static Result<bool> Rewrite(JsonObject theme, Snapshot themes, bool allowDisabled) => theme["preset"] switch
	{
		JsonValue value when value.TryGetValue<string>(out var name) => Named(name, themes, allowDisabled) switch
		{
			JsonNode added => Replaced(theme, added),
			Error<string> error => error,
			None => false,
		},
		JsonObject inner => Rewrite(inner, themes, allowDisabled),
		_ => false,
	};

	private static bool Replaced(JsonObject theme, JsonNode preset)
	{
		theme["preset"] = preset;
		return true;
	}

	/// <summary>
	/// <paramref name="resolved"/>, which reads, as the added theme <paramref name="name"/> stores it: a JSON object,
	/// a bare name as <c>{"preset":name}</c>, with its own name unless it gives one.
	/// </summary>
	private static string Written(string resolved, string name)
	{
		JsonNode? node;
		try
		{
			node = JsonNode.Parse(resolved);
		}
		catch (JsonException)
		{
			node = JsonValue.Create(resolved.Trim());
		}

		var theme = node as JsonObject ?? new JsonObject { ["preset"] = node };
		theme["name"] ??= name;
		return theme.ToJsonString(Compact);
	}

	private static Snapshot Of(LayoutThemesData stored) => new(
		stored.Added.ToImmutableDictionary(added => added.Key.ToLowerInvariant(), added => added.Value),
		[.. stored.Disabled.Select(name => name.ToLowerInvariant())]);

	private async ValueTask ChangeAsync(Func<LayoutThemesData, bool> change)
	{
		await _writeLock.WaitAsync();
		try
		{
			var stored = await data.GetExpandedServerDataAsync<LayoutThemesData>() ?? new LayoutThemesData();
			if (change(stored))
			{
				await data.SetExpandedServerDataAsync(stored);
			}
			_themes = Of(stored);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	[GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
	private static partial Regex ThemeName();
}
