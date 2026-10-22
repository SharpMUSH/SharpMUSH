using System.Text;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

[NotInParallel]
public class WikiCategoryFailureAllocationTests
{
	private const string Marker = "PRIVATE_WIKI_PAYLOAD_91C8E4";
	private const int Small = 64 * 1024;
	private const int Large = 1024 * 1024;
	private const long Unrelated = 101; // ReserveWorking(37), including its reservation owner.
	private const long DiagnosticAllowance = 64 * 1024;
	private const long GrowthAllowance = 32 * 1024;
	private const long StableSpread = 16 * 1024;
	private static long ArrayCost(long payload) => checked(32 + ((payload + 7) & ~7L));
	private static long SourceCost(int length) => checked(ArrayCost(length) + 128);

	[Test]
	[Arguments("unterminated-string")]
	[Arguments("invalid-escape")]
	[Arguments("invalid-hex")]
	[Arguments("invalid-utf8")]
	[Arguments("low-surrogate")]
	[Arguments("high-surrogate")]
	[Arguments("mismatched-surrogate")]
	[Arguments("integer-overflow")]
	[Arguments("fraction")]
	[Arguments("exponent")]
	[Arguments("depth-65")]
	[Arguments("huge-property")]
	[Arguments("literal-trailing-junk")]
	public async Task PostPreflightFailuresHaveBoundedAdditionalAllocation(string kind)
	{
		Exception? error = null;
		try
		{
			// Each size owns a fresh source and budget; fixture construction and source copies
			// are outside decode measurement. These are decoder fixtures, not native cap claims.
			var small = Measure(kind, Small);
			var large = Measure(kind, Large);
			var stable = small.Maximum - small.Minimum <= StableSpread &&
				large.Maximum - large.Minimum <= StableSpread;
			if (stable)
			{
				Check(large.Maximum - small.Minimum <= large.Scratch - small.Scratch + GrowthAllowance,
					"Allocation growth exceeded scratch growth plus the independent 32KiB allowance.");
			}
			Console.WriteLine($"{kind}: small={small.Minimum}..{small.Maximum}, large={large.Minimum}..{large.Maximum}, " +
				$"scratch={small.Scratch}..{large.Scratch}, stable-growth-check={stable}");
		}
		catch (Exception failure) { error = failure; }
		await Assert.That(error).IsNull();
	}

	private static (long Minimum, long Maximum, long Scratch) Measure(string kind, int length)
	{
		var fixture = Fixture(kind, length);
		var budget = new NativeReadBudget(1, 1, 64L * 1024 * 1024, CancellationToken.None, maxRecords: 1);
		budget.ChargeProbe();
		budget.ChargeBytes(1);
		budget.ChargeRecord();
		using var unrelated = budget.ReserveWorking(37);
		var source = OwnSource(fixture, budget, out var sourceLease);
		using (sourceLease)
		{
			var baseline = Unrelated + SourceCost(source.Length);
			var scratch = ArrayCost(source.Length);
			for (var i = 0; i < 3; i++)
			{
				var failure = Attempt(source, budget);
				CheckFailure(failure, kind);
				Baseline(budget, baseline);
			}
			var minimum = long.MaxValue;
			var maximum = 0L;
			for (var i = 0; i < 3; i++)
			{
				Exception? failure = null;
				// No assertion, delegate construction, await, fixture work or source allocation
				// occurs between the two synchronous current-thread allocation snapshots.
				var before = GC.GetAllocatedBytesForCurrentThread();
				try { using var projection = WikiCategoryProjectionDecoder.Decode(source, budget); }
				catch (Exception caught) { failure = caught; }
				var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
				CheckFailure(failure, kind);
				Check(allocated >= source.Length, "Expected post-preflight scratch allocation was not reached.");
				Check(allocated <= scratch + DiagnosticAllowance,
					"Failure exceeded scratch plus the independent warmed 64KiB allocation allowance.");
				Baseline(budget, baseline);
				minimum = Math.Min(minimum, allocated);
				maximum = Math.Max(maximum, allocated);
			}
			sourceLease.Dispose();
			Baseline(budget, Unrelated);
			unrelated.Dispose();
			Baseline(budget, 0);
			return (minimum, maximum, scratch);
		}
	}

	private static byte[] OwnSource(byte[] fixture, NativeReadBudget budget, out NativeReadReservation lease)
	{
		// Include a conservative source-owner allowance as well as the reservation owner.
		lease = budget.ReserveWorking(checked(ArrayCost(fixture.Length) + 64));
		try { return (byte[])fixture.Clone(); }
		catch { lease.Dispose(); throw; }
	}

	private static Exception? Attempt(byte[] source, NativeReadBudget budget)
	{
		try { using var projection = WikiCategoryProjectionDecoder.Decode(source, budget); return null; }
		catch (Exception failure) { return failure; }
	}

	private static void CheckFailure(Exception? failure, string kind)
	{
		Check(failure is JsonException, "Expected normalized JsonException from malformed fixture.");
		var expected = kind is "invalid-utf8" or "low-surrogate" or "high-surrogate" or "mismatched-surrogate"
			? "Invalid wiki record value." : "Invalid wiki record JSON.";
		Check(failure!.Message == expected, "Failure diagnostic changed from its fixed route-specific message.");
		Check(failure.InnerException is null, "Normalized failure retained its framework exception.");
		Check(!failure.ToString().Contains(Marker, StringComparison.Ordinal), "Failure exposed private fixture contents.");
	}

	private static void Baseline(NativeReadBudget budget, long expected)
	{
		Check(budget.WorkingBytes == expected, "Failure changed caller working reservations or leaked decoder owners.");
		Check(budget.Probes == 1 && budget.Bytes == 1 && budget.Records == 1,
			"Decoder changed already exhausted native probe/byte/record counters.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static byte[] Fixture(string kind, int length)
	{
		var padding = new string('x', length);
		var known = "{\"Title\":\"" + Marker + padding;
		var prefix = "{\"Unknown\":\"" + Marker + "\",\"RevisionNumber\":";
		var json = kind switch
		{
			"unterminated-string" => known,
			"invalid-escape" => known + "\\q\"}",
			"invalid-hex" => known + "\\u12Z4\"}",
			"invalid-utf8" => known + "\u007f\"}",
			"low-surrogate" => known + "\\uDC00\"}",
			"high-surrogate" => known + "\\uD800\"}",
			"mismatched-surrogate" => known + "\\uD800\\u0041\"}",
			"integer-overflow" => prefix + new string('9', length) + "}",
			"fraction" => prefix + "1." + new string('2', length) + "}",
			"exponent" => prefix + "1e" + new string('2', length) + "}",
			// Root object, outer array, then 63 nested arrays attempt depth 65 in Skip.
			"depth-65" => "{\"Unknown\":[\"" + Marker + padding + "\"," + new string('[', 63) + "0" + new string(']', 63) + "]}",
			"huge-property" => "{\"" + Marker + padding + "\":?}",
			// Unknown true reaches Skip; the next reader call rejects junk after that value.
			"literal-trailing-junk" => "{\"Unknown\":true" + new string(' ', length) + "x" + Marker + "}",
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};
		var bytes = Encoding.UTF8.GetBytes(json);
		if (kind == "invalid-utf8") bytes[Array.IndexOf(bytes, (byte)0x7F)] = 0xFF;
		return bytes;
	}
}
