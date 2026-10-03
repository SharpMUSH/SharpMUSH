using System.Globalization;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Every option's declared <c>Min</c>/<c>Max</c> holds on both write paths, a <c>mush.cnf</c> read and
/// <see cref="ConfigAccessor.WithValue"/>: a value outside the range is clamped to the bound and the
/// correction reported, as PennMUSH's <c>cf_int</c> clamps and logs (#1335).
/// </summary>
public class ConfigBoundsTests
{
	private sealed record OutOfRange(string Property, string Option, Type Type, object Value, object Bound, bool BelowMinimum);

	/// <summary>One value just past each declared bound that the option's type can hold.</summary>
	private static List<OutOfRange> OutOfRangeValues()
	{
		List<OutOfRange> cases = [];
		foreach (var (property, metadata) in ConfigMetadata.PropertyMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			var declared = ConfigAccessor.GetPropertyType(property)!;
			var type = Nullable.GetUnderlyingType(declared) ?? declared;
			if (metadata.Min is { } min && Representable(Convert.ToDecimal(min, CultureInfo.InvariantCulture) - 1, type) is { } below)
				cases.Add(new(property, metadata.Name, type, below, Convert.ChangeType(min, type, CultureInfo.InvariantCulture), true));
			if (metadata.Max is { } max && Representable(Convert.ToDecimal(max, CultureInfo.InvariantCulture) + 1, type) is { } above)
				cases.Add(new(property, metadata.Name, type, above, Convert.ChangeType(max, type, CultureInfo.InvariantCulture), false));
		}

		return cases;
	}

	private static object? Representable(decimal value, Type type)
	{
		try
		{
			return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
		}
		catch (OverflowException)
		{
			return null;
		}
	}

	private static PennMushConfigImport Import(IEnumerable<string> lines)
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-bounds-{Guid.NewGuid():N}.cnf");
		File.WriteAllLines(path, lines);
		try
		{
			return ReadPennMushConfig.Import(path, followIncludes: false);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task EveryDeclaredBoundIsWalked()
	{
		var cases = OutOfRangeValues();

		await Assert.That(cases.Count(c => c.BelowMinimum)).IsGreaterThan(10);
		await Assert.That(cases.Count(c => !c.BelowMinimum)).IsGreaterThan(50);
		await Assert.That(cases.Select(c => c.Property)).Contains(nameof(LimitOptions.GlobalQueueLimit));
		await Assert.That(cases.Select(c => c.Property)).Contains(nameof(LimitOptions.PlayerQueueLimit));
		await Assert.That(cases.Select(c => c.Property)).Contains(nameof(LimitOptions.CommandBurstSize));
	}

	[Test]
	public async Task WithValueClampsEveryOutOfRangeValueToItsBound()
	{
		List<string> wrong = [];
		foreach (var outside in OutOfRangeValues())
		{
			List<ConfigBoundCorrection> corrections = [];
			var updated = ConfigAccessor.WithValue(SharpMUSHOptions.Default(), outside.Property, outside.Value, corrections.Add);
			var stored = ConfigAccessor.GetValue(updated, outside.Property);
			if (!Equals(stored, outside.Bound))
				wrong.Add($"{outside.Option} {outside.Value}: stored {stored}, expected {outside.Bound}");
			if (corrections is not [{ } correction] || correction.BelowMinimum != outside.BelowMinimum
				|| !Equals(correction.Applied, outside.Bound) || correction.Option != outside.Option)
				wrong.Add($"{outside.Option} {outside.Value}: correction reported as [{string.Join(", ", corrections)}]");
		}

		await Assert.That(wrong).IsEmpty();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task MushCnfClampsEveryOutOfRangeValueToItsBound(bool belowMinimum)
	{
		var cases = OutOfRangeValues().Where(c => c.BelowMinimum == belowMinimum).ToList();
		var import = Import(cases.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Option} {c.Value}")));

		List<string> wrong = [];
		foreach (var outside in cases)
		{
			var stored = ConfigAccessor.GetValue(import.Options, outside.Property);
			if (!Equals(stored, outside.Bound))
				wrong.Add($"{outside.Option} {outside.Value}: stored {stored}, expected {outside.Bound}");
			if (!import.Skipped.Any(line => line.StartsWith($"{outside.Option} {outside.Value}:", StringComparison.Ordinal)))
				wrong.Add($"{outside.Option} {outside.Value}: clamp not reported");
		}

		await Assert.That(wrong).IsEmpty();
	}

	[Test]
	public async Task ValuesInsideTheRangeAreUntouched()
	{
		List<ConfigBoundCorrection> corrections = [];
		var defaults = SharpMUSHOptions.Default();

		var clamped = ConfigBounds.ClampAll(defaults, corrections.Add);

		await Assert.That(corrections).IsEmpty().Because("every shipped default sits inside its declared range");
		await Assert.That(clamped).IsEqualTo(defaults);
		var updated = ConfigAccessor.WithValue(defaults, nameof(LimitOptions.PlayerQueueLimit), 1u, corrections.Add);
		await Assert.That(updated.Limit.PlayerQueueLimit).IsEqualTo(1u);
		await Assert.That(corrections).IsEmpty();
	}

	/// <summary>
	/// PennMUSH's own shipped <c>mush.cnf</c> reads without a correction: a declared range that refused
	/// one of its values would be wrong about what the option means (<c>idle_timeout 0</c> is no timeout).
	/// </summary>
	[Test]
	public async Task TheShippedMushCnfSitsInsideEveryRange()
	{
		var import = ReadPennMushConfig.Import(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));

		await Assert.That(import.Skipped.Where(line => line.Contains("its minimum of") || line.Contains("its maximum of"))).IsEmpty();
	}

	/// <summary>The three limits the issue names: <c>0</c> no longer turns off the queue it limits.</summary>
	[Test]
	public async Task ZeroQueueLimitsBecomeTheMinimumOfOne()
	{
		var import = Import(["global_queue_limit 0", "player_queue_limit 0", "command_burst_size 0"]);

		await Assert.That(import.Options.Limit.GlobalQueueLimit).IsEqualTo(1u);
		await Assert.That(import.Options.Limit.PlayerQueueLimit).IsEqualTo(1u);
		await Assert.That(import.Options.Limit.CommandBurstSize).IsEqualTo(1u);
		await Assert.That(import.Skipped).Contains("player_queue_limit 0: below its minimum of 1, so 1 was used instead.");
	}
}
