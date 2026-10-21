using System.Globalization;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Migration;

/// <summary>Validated limits captured once for one category migration invocation.</summary>
internal sealed class WikiCategoryMigrationOptions
{
	internal const int ProbeLimit = 1024;
	private const string Prefix = "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_";
	private const string SourceSetting = Prefix + "MAX_SOURCE_BYTES";
	private const string WorkingSetting = Prefix + "MAX_WORKING_BYTES";
	private const string IndexOpsSetting = Prefix + "MAX_INDEX_OPS";
	private const string DeltaSetting = Prefix + "MAX_DELTA_BYTES";
	private const string KeyBatchSetting = Prefix + "MAX_KEY_BATCH_BYTES";

	private WikiCategoryMigrationOptions(int maxSourceBytes, long maxWorkingBytes, int maxIndexOps,
		int maxDeltaBytes, int maxKeyBatchBytes)
	{
		MaxSourceBytes = maxSourceBytes;
		MaxWorkingBytes = maxWorkingBytes;
		MaxIndexOps = maxIndexOps;
		MaxDeltaBytes = maxDeltaBytes;
		MaxKeyBatchBytes = maxKeyBatchBytes;
		MaxInspectedBytes = checked(maxSourceBytes + maxKeyBatchBytes + 1048576L);
	}

	internal int MaxSourceBytes { get; }
	internal long MaxWorkingBytes { get; }
	internal int MaxIndexOps { get; }
	internal int MaxDeltaBytes { get; }
	internal int MaxKeyBatchBytes { get; }
	internal long MaxInspectedBytes { get; }

	internal static WikiCategoryMigrationOptions FromEnvironment()
		=> Parse(Environment.GetEnvironmentVariable);

	internal static WikiCategoryMigrationOptions Parse(Func<string, string?> readSetting)
	{
		ArgumentNullException.ThrowIfNull(readSetting);
		var source = readSetting(SourceSetting);
		var working = readSetting(WorkingSetting);
		var indexOps = readSetting(IndexOpsSetting);
		var delta = readSetting(DeltaSetting);
		var keyBatch = readSetting(KeyBatchSetting);

		return new WikiCategoryMigrationOptions(
			checked((int)ParseSetting(source, SourceSetting, 8388608, 1, 67108864)),
			ParseSetting(working, WorkingSetting, 268435456, 1048576, 4294967296L),
			checked((int)ParseSetting(indexOps, IndexOpsSetting, 128, 1, 128)),
			checked((int)ParseSetting(delta, DeltaSetting, 262144, 1, 262144)),
			checked((int)ParseSetting(keyBatch, KeyBatchSetting, 131072, 1, 131072)));
	}

	internal NativeReadBudget CreateBudget(CancellationToken token)
		=> new(ProbeLimit, MaxInspectedBytes, MaxWorkingBytes, token);

	private static long ParseSetting(string? input, string setting, long defaultValue, long minimum, long maximum)
	{
		if (input is null) return defaultValue;
		if (input.Length == 0) throw InvalidSetting(setting, minimum, maximum);

		var value = 0L;
		foreach (var digit in input)
		{
			if (digit is < '0' or > '9') throw InvalidSetting(setting, minimum, maximum);
			try
			{
				value = checked(value * 10 + (digit - '0'));
			}
			catch (OverflowException)
			{
				throw InvalidSetting(setting, minimum, maximum);
			}
		}

		if (value < minimum || value > maximum) throw InvalidSetting(setting, minimum, maximum);
		return value;
	}

	private static ArgumentException InvalidSetting(string setting, long minimum, long maximum)
		=> new(string.Create(CultureInfo.InvariantCulture,
			$"Expected nonempty ASCII decimal digits with a value from {minimum} through {maximum}, inclusive."), setting);
}
