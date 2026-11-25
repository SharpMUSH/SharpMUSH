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

	/// <summary>Keeps <paramref name="rules"/> on the account; false when the server would not take them.</summary>
	public async Task<bool> SaveAsync(IReadOnlyList<HighlightRule> rules)
	{
		var kept = rules.Take(HighlightRule.MaxRules).ToArray();
		if (!await _settings.SetAsync(Key, kept)) return false;
		Apply(kept);
		return true;
	}

	private void OnSettingChanged(string key)
	{
		if (key == Key) Reload();
	}

	private void Reload() => Apply(_settings.Get<HighlightRule[]?>(Key, null) ?? []);

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
