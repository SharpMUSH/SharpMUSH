using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// Reads a PennMUSH-style duration from a deployment setting — <c>6h</c>, <c>30m</c>, <c>1h30m</c>,
/// <c>90d</c>, <c>45s</c> — or a bare count of seconds. Shared by every environment setting that takes
/// one, each with its own ceiling: a backup interval measured in years is a typo, a history retention
/// age measured in years is not.
/// </summary>
public static partial class DurationSetting
{
	/// <summary>
	/// Parses <paramref name="setting"/>. Empty, absent and <c>0</c> all parse successfully as
	/// <see cref="TimeSpan.Zero"/>; anything unreadable, and anything past
	/// <paramref name="maxSeconds"/>, is rejected — leaving <paramref name="duration"/> zero, so a typo
	/// turns the feature off loudly rather than picking a duration nobody asked for.
	/// </summary>
	public static bool TryParse(string? setting, long maxSeconds, out TimeSpan duration)
	{
		duration = TimeSpan.Zero;
		var text = setting?.Trim();
		if (string.IsNullOrEmpty(text)) return true;

		if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
		{
			if (seconds > maxSeconds) return false;
			duration = TimeSpan.FromSeconds(seconds);
			return true;
		}

		if (!DurationRegex().IsMatch(text)) return false;

		var total = 0L;
		foreach (Match part in DurationPartRegex().Matches(text))
		{
			if (!long.TryParse(part.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
			{
				return false;
			}

			var unitSeconds = part.Groups[2].Value.ToLowerInvariant() switch
			{
				"d" => 86400L,
				"h" => 3600L,
				"m" => 60L,
				_ => 1L
			};

			// Checked before multiplying, so an absurd count is rejected rather than wrapping.
			if (value > maxSeconds / unitSeconds) return false;
			total += value * unitSeconds;
			if (total > maxSeconds) return false;
		}

		duration = TimeSpan.FromSeconds(total);
		return true;
	}

	/// <summary>Anchored, so a setting with anything else in it is rejected rather than part-read.</summary>
	[GeneratedRegex(@"^(\d+[dhms])+$", RegexOptions.IgnoreCase)]
	private static partial Regex DurationRegex();

	[GeneratedRegex(@"(\d+)([dhms])", RegexOptions.IgnoreCase)]
	private static partial Regex DurationPartRegex();
}
