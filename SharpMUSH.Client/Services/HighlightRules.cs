using System.Text.Json;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The player's highlight rules for Play, kept on the account with the other Play settings
/// (<see cref="PlaySettings"/>, key <see cref="Key"/>), so every device the account signs in on uses the same
/// rules. The compiled form (<see cref="Matcher"/>) is rebuilt whenever they change, here or on another device.
/// </summary>
public sealed class HighlightRules : IHighlightMatcherSource, IDisposable
{
	/// <summary>The Play settings key the rules are kept under.</summary>
	public const string Key = PlayPreferences.Highlights;

	private readonly PlaySettings _settings;

	public HighlightRules(PlaySettings settings)
	{
		_settings = settings;
		_settings.Changed += OnSettingChanged;
		Reload();
	}

	/// <summary>The rules, in the order they are tried.</summary>
	public IReadOnlyList<HighlightRule> Rules { get; private set; } = [];

	/// <summary>The enabled rules, compiled.</summary>
	public HighlightMatcher Matcher { get; private set; } = HighlightMatcher.Empty;

	/// <summary>The rules changed.</summary>
	public event Action? Changed;

	/// <summary>Keeps <paramref name="rules"/> on the account.</summary>
	public async Task<HighlightSave> SaveAsync(IReadOnlyList<HighlightRule> rules)
	{
		var kept = rules.Take(HighlightRule.MaxRules).ToArray();
		if (JsonSerializer.Serialize(kept, JsonSerializerOptions.Web).Length > PlayPreferences.HighlightsMaxLength)
			return HighlightSave.TooLarge;
		var before = Rules;
		if (await _settings.SetAsync(Key, kept))
		{
			Apply(kept);
			return HighlightSave.Saved;
		}
		// The settings took the new rules in this tab before the account refused them: the ones the account keeps
		// stay in effect.
		Apply(before);
		return HighlightSave.Refused;
	}

	private void OnSettingChanged(string key)
	{
		if (key == Key) Reload();
	}

	private void Reload() => Apply(Read(_settings.Get<JsonElement[]?>(Key, null) ?? []));

	/// <summary>
	/// The rules in <paramref name="stored"/> that can be used. The value can be edited as JSON elsewhere (the
	/// account's Game preferences), so a rule that is not one (no pattern, an unknown choice, an id seen before) is
	/// left out rather than taking the others with it.
	/// </summary>
	public static IReadOnlyList<HighlightRule> Read(IEnumerable<JsonElement> stored)
	{
		var rules = new List<HighlightRule>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var element in stored)
		{
			if (rules.Count == HighlightRule.MaxRules) break;
			if (Parse(element) is { Id.Length: > 0, Pattern: not null } rule
				&& Enum.IsDefined(rule.Match) && Enum.IsDefined(rule.Lines)
				&& Enum.IsDefined(rule.Colour) && Enum.IsDefined(rule.Sound)
				&& Enum.IsDefined(rule.Target) && Enum.IsDefined(rule.Paint) && (rule.Style & ~HighlightStyle.All) == 0
				&& ids.Add(rule.Id))
				rules.Add(rule);
		}
		return rules;
	}

	private static HighlightRule? Parse(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object) return null;
		try
		{
			return element.Deserialize<HighlightRule>(JsonSerializerOptions.Web);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private void Apply(IReadOnlyList<HighlightRule> rules)
	{
		Rules = rules;
		Matcher = new HighlightMatcher(rules);
		Changed?.Invoke();
	}

	public void Dispose() => _settings.Changed -= OnSettingChanged;
}

/// <summary>Where a terminal reads the highlight rules in effect, compiled.</summary>
public interface IHighlightMatcherSource
{
	HighlightMatcher Matcher { get; }
}

/// <summary>What became of <see cref="HighlightRules.SaveAsync"/>.</summary>
public enum HighlightSave
{
	Saved,

	/// <summary>The account would not take them.</summary>
	Refused,

	/// <summary>Written out, they are longer than <see cref="PlayPreferences.HighlightsMaxLength"/>.</summary>
	TooLarge
}
