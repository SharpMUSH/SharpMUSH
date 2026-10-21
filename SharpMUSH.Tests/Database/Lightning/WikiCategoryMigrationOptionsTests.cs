using System.Globalization;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryMigrationOptionsTests
{
	private const string Prefix = "SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_";
	private static string Key(int index) => Prefix + (index switch
	{
		0 => "MAX_SOURCE_BYTES",
		1 => "MAX_WORKING_BYTES",
		2 => "MAX_INDEX_OPS",
		3 => "MAX_DELTA_BYTES",
		4 => "MAX_KEY_BATCH_BYTES",
		_ => throw new ArgumentOutOfRangeException(nameof(index))
	});
	private static long Value(WikiCategoryMigrationOptions options, int index) => index switch
	{
		0 => options.MaxSourceBytes,
		1 => options.MaxWorkingBytes,
		2 => options.MaxIndexOps,
		3 => options.MaxDeltaBytes,
		4 => options.MaxKeyBatchBytes,
		_ => throw new ArgumentOutOfRangeException(nameof(index))
	};
	private static (long Minimum, long Maximum) Bounds(int index) => index switch
	{
		0 => (1, 67108864),
		1 => (1048576, 4294967296),
		2 => (1, 128),
		3 => (1, 262144),
		4 => (1, 131072),
		_ => throw new ArgumentOutOfRangeException(nameof(index))
	};
	private static WikiCategoryMigrationOptions Parse(Dictionary<string, string?> settings)
		=> WikiCategoryMigrationOptions.Parse(name => settings.GetValueOrDefault(name));
	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}
	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
	private static void CheckRejection(Exception? failure, string key)
	{
		Check(failure is ArgumentException, "Invalid setting must throw an argument exception.");
		Check(failure!.Message.Contains(key, StringComparison.Ordinal), "Diagnostic must identify the exact setting name.");
	}

	[Test]
	public async Task MissingSettingsUseExactDefaults()
	{
		var options = Parse(new Dictionary<string, string?>());
		await Assert.That(options.MaxSourceBytes).IsEqualTo(8388608);
		await Assert.That(options.MaxWorkingBytes).IsEqualTo(268435456L);
		await Assert.That(options.MaxIndexOps).IsEqualTo(128);
		await Assert.That(options.MaxDeltaBytes).IsEqualTo(262144);
		await Assert.That(options.MaxKeyBatchBytes).IsEqualTo(131072);
		await Assert.That(options.MaxInspectedBytes).IsEqualTo(9568256L);
	}

	[Test]
	[Arguments(0, "1", 1L)]
	[Arguments(0, "67108864", 67108864L)]
	[Arguments(1, "1048576", 1048576L)]
	[Arguments(1, "4294967296", 4294967296L)]
	[Arguments(2, "1", 1L)]
	[Arguments(2, "128", 128L)]
	[Arguments(3, "1", 1L)]
	[Arguments(3, "262144", 262144L)]
	[Arguments(4, "1", 1L)]
	[Arguments(4, "131072", 131072L)]
	public async Task ExactInclusiveBoundsAreAccepted(int index, string input, long expected)
	{
		var options = Parse(new Dictionary<string, string?> { [Key(index)] = input });
		await Assert.That(Value(options, index)).IsEqualTo(expected);
	}

	[Test]
	[Arguments(0, "0")]
	[Arguments(0, "67108865")]
	[Arguments(1, "1048575")]
	[Arguments(1, "4294967297")]
	[Arguments(2, "0")]
	[Arguments(2, "129")]
	[Arguments(3, "0")]
	[Arguments(3, "262145")]
	[Arguments(4, "0")]
	[Arguments(4, "131073")]
	public async Task OutsideInclusiveBoundsRejectsWithSettingAndBothBounds(int index, string input)
	{
		var error = Capture(() =>
		{
			var failure = Capture(() => Parse(new Dictionary<string, string?> { [Key(index)] = input }));
			CheckRejection(failure, Key(index));
			var (minimum, maximum) = Bounds(index);
			Check(failure!.Message.Contains(minimum.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "Missing lower bound.");
			Check(failure.Message.Contains(maximum.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "Missing upper bound.");
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	[Arguments(0, "00000001", 1L)]
	[Arguments(1, "00004294967296", 4294967296L)]
	[Arguments(2, "00000128", 128L)]
	[Arguments(3, "00000262144", 262144L)]
	[Arguments(4, "00000131072", 131072L)]
	public async Task LeadingZerosPreserveNumericValue(int index, string input, long expected)
	{
		var options = Parse(new Dictionary<string, string?> { [Key(index)] = input });
		await Assert.That(Value(options, index)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("")]
	[Arguments(" ")]
	[Arguments("+1048576")]
	[Arguments("-1048576")]
	[Arguments(" 1048576")]
	[Arguments("1048576 ")]
	[Arguments("\t1048576")]
	[Arguments("1048576\n")]
	[Arguments("1048576\r\n")]
	[Arguments("1,048,576")]
	[Arguments("1_048_576")]
	[Arguments("1048576.0")]
	[Arguments("1e6")]
	[Arguments("0x100000")]
	[Arguments("1048576B")]
	[Arguments("１０４８５７６")]
	[Arguments("١٠٤٨٥٧٦")]
	[Arguments("1048576\0")]
	[Arguments("1048576\u00a0")]
	[Arguments("1048576\u200b")]
	[Arguments("9223372036854775808")]
	[Arguments("18446744073709551616")]
	[Arguments("PRIVATE_SETTING_SENTINEL_79B6")]
	public async Task EverySettingRejectsStrictSyntaxAndCheckedLongOverflow(string input)
	{
		var error = Capture(() =>
		{
			const string sentinel = "PRIVATE_SETTING_SENTINEL_79B6";
			for (var index = 0; index < 5; index++)
			{
				var failure = Capture(() => Parse(new Dictionary<string, string?> { [Key(index)] = input }));
				CheckRejection(failure, Key(index));
				var control = Capture(() => Parse(new Dictionary<string, string?> { [Key(index)] = sentinel }));
				CheckRejection(control, Key(index));
				// Bounds legitimately overlap numeric input; compare diagnostics across independent invalid values.
				Check(failure!.Message == control!.Message, "Diagnostic changed with invalid raw input.");
				Check(!control.ToString().Contains(sentinel, StringComparison.Ordinal), "Diagnostic exposed private control input.");
				Check(failure.InnerException is null, "Diagnostic retained an input-bearing parse exception.");
				Check(failure is not ArgumentOutOfRangeException range || range.ActualValue is null, "Diagnostic retained raw input as ActualValue.");
			}
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task LongOverflowDiagnosticDoesNotCopyPrivateInput()
	{
		var error = Capture(() =>
		{
			var privateInput = new string('9', 4096);
			for (var index = 0; index < 5; index++)
			{
				var failure = Capture(() => Parse(new Dictionary<string, string?> { [Key(index)] = privateInput }));
				CheckRejection(failure, Key(index));
				Check(!failure!.ToString().Contains(privateInput, StringComparison.Ordinal), "Diagnostic copied raw overflowing input.");
			}
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task CapturesEachExactSettingOnceAndKeepsImmutableValues()
	{
		var settings = new Dictionary<string, string?>
		{
			[Key(0)] = "23", [Key(1)] = "4294967296", [Key(2)] = "17",
			[Key(3)] = "19", [Key(4)] = "29"
		};
		var reads = new Dictionary<string, int>();
		var options = WikiCategoryMigrationOptions.Parse(name =>
		{
			reads[name] = reads.GetValueOrDefault(name) + 1;
			return settings.GetValueOrDefault(name);
		});
		settings.Clear();
		var error = Capture(() =>
		{
			Check(reads.Count == 5, "Parse read an unexpected setting.");
			for (var index = 0; index < 5; index++) Check(reads.GetValueOrDefault(Key(index)) == 1, "Setting was not captured exactly once.");
			Check(options.MaxSourceBytes == 23 && options.MaxWorkingBytes == 4294967296L && options.MaxIndexOps == 17 && options.MaxDeltaBytes == 19 && options.MaxKeyBatchBytes == 29, "Options changed after callback backing data changed.");
			Check(options.MaxInspectedBytes == 1048628L, "Derived limit did not use captured values.");
			var budget = options.CreateBudget(CancellationToken.None);
			budget.ChargeBytes(1048628);
			Check(Capture(() => budget.ChargeBytes(1)) is NativeReadLimitException { Dimension: "bytes" }, "Budget lost immutable captured inspected limit.");
			Check(reads.Values.All(count => count == 1), "Property/budget access reread settings.");
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task NullCallbackRejectsAsArgumentNull()
	{
		var failure = Capture(() => WikiCategoryMigrationOptions.Parse(null!));
		await Assert.That(failure is ArgumentNullException).IsTrue();
	}

	[Test]
	public async Task CallbackFailurePropagatesUnchanged()
	{
		var expected = new InvalidOperationException("Synthetic callback failure.");
		var failure = Capture(() => WikiCategoryMigrationOptions.Parse(_ => throw expected));
		await Assert.That(ReferenceEquals(failure, expected)).IsTrue();
	}

	[Test]
	[Arguments(1, 1, 1048576L, 1048578L)]
	[Arguments(8388608, 131072, 268435456L, 9568256L)]
	[Arguments(67108864, 131072, 4294967296L, 68288512L)]
	public async Task RealBudgetEnforcesExactDerivedLimitsWithoutExtraRecordCap(int source, int keyBatch, long working, long inspected)
	{
		var error = Capture(() =>
		{
			var options = Parse(new Dictionary<string, string?>
			{
				[Key(0)] = source.ToString(CultureInfo.InvariantCulture),
				[Key(1)] = working.ToString(CultureInfo.InvariantCulture),
				[Key(2)] = "1", [Key(4)] = keyBatch.ToString(CultureInfo.InvariantCulture)
			});
			Check(options.MaxInspectedBytes == inspected, "Derived inspected-byte limit differs from independent fixture.");
			var budget = options.CreateBudget(CancellationToken.None);
			Check(budget.Probes == 0 && budget.Bytes == 0 && budget.Records == 0 && budget.WorkingBytes == 0, "Budget began charged.");
			for (var i = 0; i < 1024; i++) budget.ChargeProbe();
			Check(Capture(budget.ChargeProbe) is NativeReadLimitException { Dimension: "probes" }, "Probe bound differs from 1024.");
			budget.ChargeBytes(inspected);
			Check(Capture(() => budget.ChargeBytes(1)) is NativeReadLimitException { Dimension: "bytes" }, "Inspected-byte boundary was widened.");
			using (var lease = budget.ReserveWorking(working - 64))
			{
				Check(budget.WorkingBytes == working, "Working-byte bound was truncated or narrowed.");
				Check(Capture(() => { using var excess = budget.ReserveWorking(0); }) is NativeReadLimitException { Dimension: "working-bytes" }, "Working-byte boundary was widened.");
			}
			Check(budget.WorkingBytes == 0, "Working reservation did not release.");
			for (var i = 0; i < 1025; i++) budget.ChargeRecord();
			Check(budget.Records == 1025, "Factory added an unrelated record cap.");
			var fresh = options.CreateBudget(CancellationToken.None);
			Check(fresh.Probes == 0 && fresh.Bytes == 0 && fresh.Records == 0 && fresh.WorkingBytes == 0, "Factory reused charged mutable budget.");
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task RealBudgetPreservesCancellationTokenAcrossEveryAdmission()
	{
		var error = Capture(() =>
		{
			using var source = new CancellationTokenSource();
			var options = Parse(new Dictionary<string, string?>());
			var budget = options.CreateBudget(source.Token);
			source.Cancel();
			Check(budget.CancellationToken == source.Token, "Factory replaced caller token.");
			Action[] admissions = [budget.ChargeProbe, () => budget.ChargeBytes(1), budget.ChargeRecord, () => { using var lease = budget.ReserveWorking(1); }];
			foreach (var admission in admissions)
				Check(Capture(admission) is OperationCanceledException canceled && canceled.CancellationToken == source.Token, "Admission lost caller cancellation.");
			Check(budget.Probes == 0 && budget.Bytes == 0 && budget.Records == 0 && budget.WorkingBytes == 0, "Canceled admission charged budget.");
		});
		await Assert.That(error).IsNull();
	}
}
