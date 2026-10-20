using System.Text;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

[NotInParallel]
public class WikiCategoryNativeBudgetTests
{
	private const string Json = "{\"Title\":\"Page\",\"AuthorDbref\":\"#1\",\"Published\":true,\"Categories\":[\"a\",\"A\",\"😀\"]}";
	// Independent fixture model: C=3, UTF-16 units=4, title=4, author=2.
	private const long Retained = 528;
	private const long Unrelated = 101; // ReserveWorking(37), including its 64-byte owner.
	private static long ArrayCost(long payload) => checked(32 + ((payload + 7) & ~7L));
	private static long Scratch(string json) => ArrayCost(Encoding.UTF8.GetByteCount(json));
	private static long Source(string json) => checked(Scratch(json) + 128); // Array, source owner, reservation.
	private static long Peak(string json, long retained, long prior = 0) => checked(prior + Source(json) + Scratch(json) + 64 + retained + 64);
	private static NativeReadBudget Budget(long maximum, CancellationToken token = default)
		=> new(1, 1, maximum, token, maxRecords: 0);

	private static byte[] OwnSource(string json, NativeReadBudget budget, out NativeReadReservation lease)
	{
		// The fixture text exists outside the ownership boundary; admit the byte copy first.
		lease = budget.ReserveWorking(checked(Scratch(json) + 64));
		try { return Encoding.UTF8.GetBytes(json); }
		catch { lease.Dispose(); throw; }
	}

	private static WikiCategoryProjection Decode(byte[] source, NativeReadBudget budget)
		=> WikiCategoryProjectionDecoder.Decode(source, budget) ?? throw new InvalidOperationException("Expected projection.");
	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}
	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
	private static void Baseline(NativeReadBudget budget, long expected)
	{
		Check(budget.WorkingBytes == expected, "Outstanding working bytes differ from independent model.");
		Check(budget.Probes == 0 && budget.Bytes == 0 && budget.Records == 0, "Decoder charged native read work.");
	}

	[Test]
	public async Task SuccessfulRetentionAndIdempotentDisposal()
	{
		var error = Capture(() =>
		{
			var budget = Budget(Peak(Json, Retained, Unrelated));
			using var unrelated = budget.ReserveWorking(37);
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				var baseline = Unrelated + Source(Json);
				using var projection = Decode(source, budget);
				Check(projection.RetainedBytes == Retained, "Retained metadata differs from fixture formula.");
				Check(projection.PeakReservedBytes == 0, "Native path must not report an independent numeric peak.");
				Baseline(budget, baseline + Retained + 64);
				projection.Dispose();
				projection.Dispose();
				Baseline(budget, baseline);
				Check(Capture(() => { _ = projection.CategoryCount; }) is ObjectDisposedException, "Disposed storage remains accessible.");
			}
			Baseline(budget, Unrelated);
			unrelated.Dispose();
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task ExactSharedPeakDoesNotChargeSourceTwice()
	{
		var error = Capture(() =>
		{
			var budget = Budget(Peak(Json, Retained));
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			using (var projection = Decode(source, budget))
			{
				Check(projection.Title == "Page" && projection.CategoryCount == 3, "Exact-boundary content changed.");
				Baseline(budget, Source(Json) + Retained + 64);
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task SharedPeakMinusOneRejectsAndPreservesCallerOwners()
	{
		var error = Capture(() =>
		{
			var budget = Budget(Peak(Json, Retained, Unrelated) - 1);
			using var unrelated = budget.ReserveWorking(37);
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				var failure = Capture(() => { using var projection = Decode(source, budget); });
				Check(failure is NativeReadLimitException { Dimension: "working-bytes" }, "Retained admission must preserve native limit exception.");
				Baseline(budget, Unrelated + Source(Json));
			}
			Baseline(budget, Unrelated);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task ScratchOwnerMinusOneRejectsAndPreservesSource()
	{
		var error = Capture(() =>
		{
			var budget = Budget(Source(Json) + Scratch(Json) + 63);
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				var failure = Capture(() => { using var projection = Decode(source, budget); });
				Check(failure is NativeReadLimitException { Dimension: "working-bytes" }, "Scratch admission must preserve native limit exception.");
				Baseline(budget, Source(Json));
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task KnownWrongTypeAfterScratchReleasesDecoderOwners()
	{
		var error = Capture(() =>
		{
			const string malformed = "{\"Title\":42}";
			var budget = Budget(Peak(malformed, Retained));
			var source = OwnSource(malformed, budget, out var sourceLease);
			using (sourceLease)
			{
				Check(Capture(() => { using var projection = Decode(source, budget); }) is JsonException, "Wrong known-field type must reject as JSON.");
				Baseline(budget, Source(malformed));
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task RootNullNeedsOnlyScratchAndReturnsToCallerBaseline()
	{
		var error = Capture(() =>
		{
			const string json = "null";
			var budget = Budget(Source(json) + Scratch(json) + 64);
			var source = OwnSource(json, budget, out var sourceLease);
			using (sourceLease)
			{
				using var projection = WikiCategoryProjectionDecoder.Decode(source, budget);
				Check(projection is null, "JSON null changed.");
				Baseline(budget, Source(json));
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task SharedBudgetKeepsTwoProjectionOwnersIndependent()
	{
		var error = Capture(() =>
		{
			var budget = Budget(Peak(Json, Retained) + Retained + 64);
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				using var first = Decode(source, budget);
				using var second = Decode(source, budget);
				Baseline(budget, Source(Json) + 2 * (Retained + 64));
				first.Dispose();
				Baseline(budget, Source(Json) + Retained + 64);
				Check(second.CategoryAt(2).SequenceEqual("😀"), "Disposing first projection damaged second.");
				second.Dispose();
				Baseline(budget, Source(Json));
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task CategorySlotsDoNotConsumeNativeRecordAllowance()
	{
		var error = Capture(() =>
		{
			var json = "{\"Categories\":[" + string.Join(',', Enumerable.Repeat("\"a\"", 129)) + "]}";
			// No explicit title/author: five arrays and the projection owner only.
			var retained = 128 + ArrayCost(129 * 16) + 2 * ArrayCost(129 * 2) + ArrayCost(129 * 4) + ArrayCost(129);
			var budget = Budget(Peak(json, retained));
			var source = OwnSource(json, budget, out var sourceLease);
			using (sourceLease)
			using (var projection = Decode(source, budget))
			{
				Check(projection.CategoryCount == 129 && projection.DistinctCategoryCount == 1, "Native record cap leaked into category count.");
				Check(projection.RetainedBytes == retained, "129-slot retained formula changed.");
				Baseline(budget, Source(json) + retained + 64);
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task PreCancellationUsesBudgetTokenAndPreservesExistingSource()
	{
		var error = Capture(() =>
		{
			using var cts = new CancellationTokenSource();
			var budget = Budget(Peak(Json, Retained), cts.Token);
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				cts.Cancel(); // Source was already admitted; no reservation follows cancellation.
				var failure = Capture(() => { using var projection = Decode(source, budget); });
				Check(failure is OperationCanceledException canceled && canceled.CancellationToken == cts.Token, "Cancellation token or exception changed.");
				Baseline(budget, Source(Json));
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task MalformedMegabyteTailHasFixedDiagnosticAndAllocation()
	{
		var error = Capture(() =>
		{
			const string marker = "SYNTHETIC_PRIVATE_TAIL_91C8E4";
			var json = "truX" + marker + new string('A', 1048576);
			// Source-only capacity proves preflight occurs before scratch admission.
			var budget = Budget(Source(json));
			var source = OwnSource(json, budget, out var sourceLease);
			using (sourceLease)
			{
				for (var i = 0; i < 3; i++)
				{
					Check(Capture(() => { using var projection = Decode(source, budget); }) is JsonException, "Malformed warmup did not reject as JSON.");
					Baseline(budget, Source(json));
				}
				JsonException? failure = null;
				var before = GC.GetAllocatedBytesForCurrentThread();
				try { using var projection = Decode(source, budget); }
				catch (JsonException ex) { failure = ex; }
				var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
				if (failure is null) throw new InvalidOperationException("Malformed tail accepted.");
				Check(failure.Message.Length <= 1024 && !failure.Message.Contains(marker, StringComparison.Ordinal), "Diagnostic exposed private input or exceeded fixed length.");
				Check(allocated <= 65536, "Malformed input exceeded independent warmed 64KiB allocation ceiling.");
				Baseline(budget, Source(json));
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task NativeContentMatchesRealCodecIncludingFirstDistinctOrdinals()
	{
		var error = Capture(() =>
		{
			var budget = Budget(Peak(Json, Retained));
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				var oracle = Codec.Deserialize<WikiPageRecord>(source);
				using var projection = Decode(source, budget);
				Check(projection.Title == oracle.Title && projection.AuthorDbref == oracle.AuthorDbref && projection.Published == oracle.Published, "Scalar Codec mismatch.");
				var categories = oracle.Categories!;
				Check(!projection.CategoriesAreNull && projection.CategoryCount == categories.Length, "Codec category shape mismatch.");
				for (var i = 0; i < categories.Length; i++) Check(!projection.IsNullCategory(i) && projection.CategoryAt(i).SequenceEqual(categories[i]), "Codec category text mismatch.");
				var ordinals = Enumerable.Range(0, categories.Length).DistinctBy(i => categories[i].ToUpperInvariant()).ToArray();
				Check(projection.DistinctCategoryCount == ordinals.Length, "Distinct Codec count mismatch.");
				for (var i = 0; i < ordinals.Length; i++) Check(projection.DistinctCategoryOrdinalAt(i) == ordinals[i], "First distinct Codec ordinal mismatch.");
				Baseline(budget, Source(Json) + Retained + 64);
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task NativeCountersAlreadyExhaustedRemainUnchanged()
	{
		var error = Capture(() =>
		{
			var budget = new NativeReadBudget(1, 1, Peak(Json, Retained), CancellationToken.None, maxRecords: 1);
			budget.ChargeProbe();
			budget.ChargeBytes(1);
			budget.ChargeRecord();
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				using var projection = Decode(source, budget);
				Check(budget.Probes == 1 && budget.Bytes == 1 && budget.Records == 1, "Decode changed exhausted native work counters.");
				Check(budget.WorkingBytes == Source(Json) + Retained + 64, "Shared source/retention total changed.");
			}
			Check(budget.WorkingBytes == 0, "Native owners leaked after disposal.");
		});
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task CancellationAfterReturnDoesNotPreventProjectionCleanup()
	{
		var error = Capture(() =>
		{
			using var cts = new CancellationTokenSource();
			var budget = Budget(Peak(Json, Retained), cts.Token);
			var source = OwnSource(Json, budget, out var sourceLease);
			using (sourceLease)
			{
				using var projection = Decode(source, budget);
				cts.Cancel();
				projection.Dispose();
				projection.Dispose();
				Baseline(budget, Source(Json));
				Check(Capture(() => { _ = projection.Title; }) is ObjectDisposedException, "Canceled projection remains accessible after disposal.");
			}
			Baseline(budget, 0);
		});
		await Assert.That(error).IsNull();
	}
}
