using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Configuration;

/// <summary>
/// The two recover-list retention settings, <c>recover_keep_players</c> and <c>recover_keep_objects</c>: either
/// <c>forever</c> or a time value of at least a day, so a typo such as <c>0</c> or <c>10m</c> cannot empty the
/// recover list.
/// </summary>
public static partial class RecoverRetention
{
	public const string Forever = "forever";

	/// <summary><c>recover_keep_objects</c>' default.</summary>
	public const string ObjectsDefault = "30d";

	/// <summary>The shape @config accepts; <see cref="TryParse"/> also refuses a time shorter than <see cref="Shortest"/>.</summary>
	public const string Pattern = @"^\s*(forever|(\d+[dhmsDHMS])+)\s*$";

	/// <summary>The shortest retention a setting may name.</summary>
	public static readonly TimeSpan Shortest = TimeSpan.FromDays(1);

	/// <summary>
	/// Reads a setting: <paramref name="keep"/> is null for <c>forever</c>. False for anything else that is not a time
	/// of at least <see cref="Shortest"/>.
	/// </summary>
	public static bool TryParse(string? setting, out TimeSpan? keep)
	{
		keep = null;
		var text = setting?.Trim() ?? string.Empty;
		if (text.Equals(Forever, StringComparison.OrdinalIgnoreCase)) return true;
		if (!PatternRegex().IsMatch(text)) return false;

		var seconds = 0L;
		foreach (Match part in PartRegex().Matches(text))
		{
			if (!long.TryParse(part.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
				|| value > 100L * 365)
			{
				return false;
			}

			seconds += value * char.ToLowerInvariant(part.Groups[2].Value[0]) switch
			{
				'd' => 86400L,
				'h' => 3600L,
				'm' => 60L,
				_ => 1L
			};
		}

		var duration = TimeSpan.FromSeconds(seconds);
		if (duration < Shortest) return false;
		keep = duration;
		return true;
	}

	[GeneratedRegex(Pattern)]
	private static partial Regex PatternRegex();

	[GeneratedRegex(@"(\d+)([dhmsDHMS])")]
	private static partial Regex PartRegex();
}
