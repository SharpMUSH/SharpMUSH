using System.Globalization;

namespace SharpMUSH.Database.Lightning.Migration;

/// <summary>Private migration capacity failure retaining the observed native requirement.</summary>
internal sealed class WikiCategoryMigrationCapacityException : Exception
{
	internal WikiCategoryMigrationCapacityException(string dimension, long requiredBytes, long configuredBytes,
		bool requiresEnvelopeReview = false)
		: base(MessageFor(dimension, requiredBytes, configuredBytes, requiresEnvelopeReview))
	{
		Dimension = dimension;
		RequiredBytes = requiredBytes;
		ConfiguredBytes = configuredBytes;
		SettingName = SettingFor(dimension);
		RequiresEnvelopeReview = requiresEnvelopeReview;
	}

	public string Dimension { get; }
	public long RequiredBytes { get; }
	public long ConfiguredBytes { get; }
	public string SettingName { get; }
	public bool RequiresEnvelopeReview { get; }

	private static string SettingFor(string dimension) => dimension switch
	{
		"source-bytes" => "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_SOURCE_BYTES",
		"key-bytes" => "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_KEY_BATCH_BYTES",
		_ => throw new ArgumentOutOfRangeException(nameof(dimension))
	};

	private static string MessageFor(string dimension, long requiredBytes, long configuredBytes, bool requiresEnvelopeReview)
		=> string.Create(CultureInfo.InvariantCulture,
			$"Wiki category migration capacity: {SettingFor(dimension)} requires {requiredBytes} bytes; configured {configuredBytes} bytes.")
			+ (requiresEnvelopeReview ? " The bounded implementation envelope requires review; increasing the setting above its ceiling is unsupported." : "");
}
