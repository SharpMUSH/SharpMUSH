using System.Globalization;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Library.Models;

/// <summary>
/// The retention policy for every kind of history the game keeps — wiki revisions, scene pose edits,
/// soft-deleted poses — plus how a pass runs. A deployment concern, like the backup settings, so it is
/// read from the server's environment rather than <c>@config</c>.
///
/// <para>Every default keeps everything and schedules nothing: a game that sets none of this never
/// loses a version, and a purge only ever happens because an operator asked for one.</para>
/// </summary>
public sealed record HistoryRetentionOptions
{
	/// <summary>Prefix of every setting read by <see cref="Resolve"/>.</summary>
	public const string EnvironmentPrefix = "SHARPMUSH_HISTORY_";

	/// <summary>Longest age a setting may name: a century. Anything longer is a typo, not a policy.</summary>
	private const long MaxAgeSeconds = 100L * 365 * 86400;

	/// <summary>Longest scheduled interval: the same one-year ceiling the backup schedule has.</summary>
	private const long MaxIntervalSeconds = 365L * 86400;

	/// <summary>The rule for each kind, by <see cref="Services.Interfaces.IHistoryStore.Kind"/>. A kind
	/// with no entry keeps everything.</summary>
	public IReadOnlyDictionary<string, HistoryRetentionRule> Rules { get; init; }
		= new Dictionary<string, HistoryRetentionRule>(StringComparer.OrdinalIgnoreCase);

	/// <summary>How often a scheduled pass runs. Zero (the default) leaves scheduling off;
	/// <c>@storage/purge</c> still runs one on demand.</summary>
	public TimeSpan Interval { get; init; } = TimeSpan.Zero;

	/// <summary>
	/// The most records one write transaction deletes. A pass is a series of these, each its own commit,
	/// so the writer thread is never held for longer than one batch and every other write interleaves.
	/// </summary>
	public int BatchSize { get; init; } = 256;

	/// <summary>
	/// Where purged records are appended, as JSON lines, before they are deleted. Unset (the default)
	/// archives nothing; the record is then gone from the live world, but still in every backup taken
	/// before the pass.
	/// </summary>
	public string ArchivePath { get; init; } = string.Empty;

	/// <summary>The rule for <paramref name="kind"/>, or <see cref="HistoryRetentionRule.KeepEverything"/>.</summary>
	public HistoryRetentionRule RuleFor(string kind)
		=> Rules.TryGetValue(kind, out var rule) ? rule : HistoryRetentionRule.KeepEverything;

	/// <summary>
	/// The environment variable name a kind's settings start with: <c>scene.edits</c> becomes
	/// <c>SHARPMUSH_HISTORY_SCENE_EDITS</c>, read with <c>_KEEP</c> and <c>_MAX_AGE</c> appended.
	/// </summary>
	public static string SettingNameFor(string kind)
		=> EnvironmentPrefix + new string(kind.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

	/// <summary>
	/// Reads the policy for <paramref name="kinds"/> from <paramref name="environment"/>. An unreadable
	/// setting is logged and treated as unset — which keeps more, never less, so a typo can only ever
	/// fail safe.
	/// </summary>
	public static HistoryRetentionOptions Resolve(IEnumerable<string> kinds, Func<string, string?> environment,
		ILogger logger)
	{
		var rules = new Dictionary<string, HistoryRetentionRule>(StringComparer.OrdinalIgnoreCase);
		foreach (var kind in kinds.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var name = SettingNameFor(kind);
			var keep = ReadKeep(name + "_KEEP", environment, logger);
			var maxAge = ReadMaxAge(name + "_MAX_AGE", environment, logger);
			rules[kind] = new HistoryRetentionRule { KeepNewest = keep, MaxAge = maxAge };
		}

		var intervalName = EnvironmentPrefix + "INTERVAL";
		var intervalSetting = environment(intervalName);
		if (!DurationSetting.TryParse(intervalSetting, MaxIntervalSeconds, out var interval))
		{
			logger.LogWarning(
				"{Setting} is set to '{Value}', which is not an interval like 1d or 6h; scheduled history retention stays off",
				intervalName, intervalSetting);
		}

		var batchName = EnvironmentPrefix + "BATCH";
		var batchSetting = environment(batchName);
		var batch = 256;
		if (!string.IsNullOrWhiteSpace(batchSetting))
		{
			if (int.TryParse(batchSetting, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
			{
				batch = parsed;
			}
			else
			{
				logger.LogWarning("{Setting} is set to '{Value}', which is not a positive count; using {Default}",
					batchName, batchSetting, batch);
			}
		}

		return new HistoryRetentionOptions
		{
			Rules = rules,
			Interval = interval,
			BatchSize = batch,
			ArchivePath = environment(EnvironmentPrefix + "ARCHIVE_PATH")?.Trim() ?? string.Empty
		};
	}

	private static int ReadKeep(string name, Func<string, string?> environment, ILogger logger)
	{
		var setting = environment(name);
		if (string.IsNullOrWhiteSpace(setting)) return 0;
		if (int.TryParse(setting, NumberStyles.None, CultureInfo.InvariantCulture, out var keep) && keep >= 0) return keep;

		logger.LogWarning("{Setting} is set to '{Value}', which is not a count; that bound stays off", name, setting);
		return 0;
	}

	private static TimeSpan? ReadMaxAge(string name, Func<string, string?> environment, ILogger logger)
	{
		var setting = environment(name);
		if (string.IsNullOrWhiteSpace(setting)) return null;
		if (DurationSetting.TryParse(setting, MaxAgeSeconds, out var age) && age > TimeSpan.Zero) return age;

		logger.LogWarning("{Setting} is set to '{Value}', which is not an age like 90d or 12h; that bound stays off",
			name, setting);
		return null;
	}
}
