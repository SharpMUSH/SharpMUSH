using System.Reflection;
using System.Text;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;

namespace SharpMUSH.Tests.Database.Lightning;

[NotInParallel]
public partial class WikiCategoryProjectionTests
{
	// Each synchronous check completes before TUnit assertions; no await or assertion
	// machinery runs inside a current-thread allocation measurement interval.
	[Test]
	[MethodDataSource(nameof(DifferentialCases))]
	public async Task MatchesCodec(string name, byte[] bytes, string expected)
	{
		var failure = Capture(() => CompareWithCodec((name, bytes, expected)));
		await Assert.That(failure).IsNull();
	}

	[Test]
	[MethodDataSource(nameof(AuxiliaryCases))]
	public async Task PreservesOwnershipAndAdmission(string name)
	{
		var failure = Capture(() => RunAuxiliary(name));
		await Assert.That(failure).IsNull();
	}

	private static string? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception ex) { return ex.ToString(); }
	}

	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void CompareWithCodec((string Name, byte[] Bytes, string Expected) item)
	{
		WikiPageRecord? oracle = null;
		var rejected = false;
		try { oracle = Codec.Deserialize<WikiPageRecord>(item.Bytes); }
		catch (JsonException) { rejected = true; }
		var outcome = rejected ? "reject" : oracle is null ? "null" : "record";
		Console.WriteLine("ORACLE " + item.Name + " " + outcome);
		Check(item.Expected == "oracle" || outcome == item.Expected, "Frozen oracle outcome changed");
		WikiCategoryProjection? projection;
		try { projection = WikiCategoryProjectionDecoder.Decode(item.Bytes, 64L * 1024 * 1024); }
		catch (JsonException) { Check(rejected, "Candidate rejected oracle-accepted input"); return; }
		Check(!rejected, "Candidate accepted oracle-rejected input");
		if (oracle is null) { Check(projection is null, "Root null changed"); return; }
		Check(projection is not null, "Missing projection");
		using var p = projection!;
		Check(p.Title == oracle.Title && p.AuthorDbref == oracle.AuthorDbref && p.Published == oracle.Published, "Projected scalar mismatch");
		Check(p.CategoriesAreNull == (oracle.Categories is null) && p.CategoryCount == (oracle.Categories?.Length ?? 0), "Category shape mismatch");
		var categories = oracle.Categories ?? [];
		for (var i = 0; i < categories.Length; i++)
		{
			Check(p.IsNullCategory(i) == (categories[i] is null), "Null category mismatch");
			if (categories[i] is null) Expect<InvalidOperationException>(() => { _ = p.CategoryAt(i).Length; });
			else Check(p.CategoryAt(i).SequenceEqual(categories[i]), "Category text mismatch");
		}
		if (categories.Any(c => c is null)) Expect<InvalidOperationException>(() => { _ = p.DistinctCategoryCount; });
		else
		{
			var ordinals = Enumerable.Range(0, categories.Length).DistinctBy(i => categories[i].ToUpperInvariant()).ToArray();
			Check(p.DistinctCategoryCount == ordinals.Length, "Distinct count mismatch");
			for (var i = 0; i < ordinals.Length; i++) Check(p.DistinctCategoryOrdinalAt(i) == ordinals[i], "Distinct first ordinal mismatch");
		}
		Check(p.RetainedBytes > 0 && p.PeakReservedBytes >= p.RetainedBytes && p.PeakReservedBytes <= 64L * 1024 * 1024, "Reservation invariants failed");
		p.Dispose();
		Expect<ObjectDisposedException>(() => { _ = p.Title; });
		Expect<ObjectDisposedException>(() => { _ = p.CategoryCount; });
		Expect<ObjectDisposedException>(() => { _ = p.CategoryAt(0).Length; });

	}

	private static void RunAuxiliary(string selected)
	{
		var strings = StringMembers;
		var matched = 0;
		void Run(string name, Action action)
		{
			if (name != selected) return;
			Check(++matched == 1, "Duplicate auxiliary case: " + selected);
			action();
		}
		Run("schema-member-serializer-guard", () =>
		{
			var expected = strings.Concat(new[] { "RevisionNumber", "Categories", "Published" }).Order().ToArray();
			var properties = typeof(WikiPageRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);
			Check(properties.Select(p => p.Name).Order().SequenceEqual(expected), "WikiPageRecord schema changed");
			foreach (var p in properties)
			{
				var type = p.Name switch { "Categories" => typeof(string[]), "Published" => typeof(bool?), "RevisionNumber" => typeof(int), _ => typeof(string) };
				Check(p.PropertyType == type, "Property type changed: " + p.Name);
			}
			var context = LightningJsonContext.Default;
			var info = context.GetTypeInfo(typeof(WikiPageRecord))!;
			Check(info.Properties.Select(p => p.Name).Order().SequenceEqual(expected), "Serializer members changed");
			Check(!context.Options.PropertyNameCaseInsensitive && context.Options.PropertyNamingPolicy is null && !context.Options.AllowTrailingCommas && context.Options.ReadCommentHandling == JsonCommentHandling.Disallow && context.Options.MaxDepth is 0 or 64, "Serializer policy changed");
		});
		Run("pre-cancel", () => Expect<OperationCanceledException>(() => WikiCategoryProjectionDecoder.Decode(Encoding.UTF8.GetBytes("{}"), 1024, new CancellationToken(true))));
		Run("budget-source-admission", () => Expect<WikiCategoryProjectionCapacityException>(() => WikiCategoryProjectionDecoder.Decode(Encoding.UTF8.GetBytes("{}"), 0)));
		// Empirical allocation smoke checks: the input and fixtures are prepared outside the interval.
		// A fixed 4096-byte allowance covers incidental runtime bookkeeping after warmup; excess
		// is a finding, not evidence that the self-reported reservation counter proves a bound.
		foreach (var sample in new[]
		{
			(Name: "large-ignored-body", Json: JsonSerializer.Serialize(new { MarkdownSource = new string('x', 1048576) })),
			(Name: "repeated-known-strings", Json: "{" + string.Join(',', Enumerable.Repeat("\"MarkdownSource\":\"" + new string('x', 4096) + "\"", 1024)) + "}"),
			(Name: "repeated-tiny-categories", Json: JsonSerializer.Serialize(new { Categories = Enumerable.Repeat("a", 65536).ToArray() }))
		})
			Run("allocation-smoke-" + sample.Name, () =>
			{
				var bytes = Encoding.UTF8.GetBytes(sample.Json);
				for (var i = 0; i < 3; i++) WikiCategoryProjectionDecoder.Decode(bytes, 64L * 1024 * 1024)!.Dispose();
				var before = GC.GetAllocatedBytesForCurrentThread();
				var projection = WikiCategoryProjectionDecoder.Decode(bytes, 64L * 1024 * 1024)!;
				var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
				var peak = projection.PeakReservedBytes;
				var retained = projection.RetainedBytes;
				projection.Dispose();
				var inputCharge = checked(32L + ((bytes.LongLength + 7) & ~7L));
				var allowed = checked(peak - inputCharge + 4096);
				Console.WriteLine($"ALLOCATION {sample.Name} allocated={allocated} peak={peak} retained={retained} externalInputCharge={inputCharge} fixedAllowance=4096 allowed={allowed}");
				Check(retained > 0 && peak >= retained && peak <= 64L * 1024 * 1024, "Smoke reservation invariants failed");
				Check(allocated <= allowed, "EMPIRICAL ALLOCATION EXCESS: managed allocation exceeds peak minus externally owned source plus fixed allowance");
			});
		// Failure-path allocation is measured against a fixed independent limit, not counters.
		foreach (var literal in new[] { "truX", "falsX", "nulX" })
			foreach (var nested in new[] { false, true })
				Run("malformed-literal-private-tail-allocation-" + literal + (nested ? "-nested" : "-root"), () =>
				{
					const string privateMarker = "SYNTHETIC_PRIVATE_TAIL_91C8E4";
					var bytes = Encoding.UTF8.GetBytes((nested ? "{\"unknown\":" : "") + literal + privateMarker + new string('A', 1048576) + (nested ? "}" : ""));
					for (var i = 0; i < 3; i++)
					{
						try { WikiCategoryProjectionDecoder.Decode(bytes, 64L * 1024 * 1024)?.Dispose(); }
						catch (JsonException) { }
					}
					JsonException? failure = null;
					var before = GC.GetAllocatedBytesForCurrentThread();
					try { WikiCategoryProjectionDecoder.Decode(bytes, 64L * 1024 * 1024)?.Dispose(); }
					catch (JsonException ex) { failure = ex; }
					var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
					Console.WriteLine($"FAILURE_ALLOCATION {literal} nested={nested} allocated={allocated} independentLimit=65536 messageLength={failure?.Message.Length}");
					if (failure is null) throw new InvalidOperationException("Malformed literal was accepted");
					Check(failure.Message.Length <= 1024 && !failure.Message.Contains(privateMarker, StringComparison.Ordinal), "Malformed input diagnostic contains private tail or exceeds fixed message limit");
					Check(allocated <= 65536, "EMPIRICAL FAILURE ALLOCATION EXCESS: malformed literal allocated above independent 64KiB limit");
				});
		// Admission expectations come from the pinned charge contract and real Codec values.
		var admissionBytes = JsonSerializer.SerializeToUtf8Bytes(new
		{
			Title = new string('T', 32769),
			AuthorDbref = new string('A', 16385),
			Categories = Enumerable.Range(0, 513).Select(i => "category-" + i + "-σ😀").Concat(new[] { "CATEGORY-0-Σ😀", "" }).ToArray()
		});
		var admissionOracle = Codec.Deserialize<WikiPageRecord>(admissionBytes);
		var admissionCategories = admissionOracle.Categories!;
		var admissionC = admissionCategories.LongLength;
		var admissionUnits = admissionCategories.Sum(c => (long)c.Length);
		long ArrayCharge(long payload) => checked(32 + ((payload + 7) & ~7L));
		long StringCharge(string? text) => text is null ? 0 : checked(64 + (((2L * text.Length + 2) + 7) & ~7L));
		var sourceCharge = ArrayCharge(admissionBytes.LongLength);
		var initialCharge = checked(2 * sourceCharge + 256);
		var expectedRetained = checked(128 + ArrayCharge(16 * admissionC) + 2 * ArrayCharge(2 * admissionUnits) + ArrayCharge(4 * admissionC) + ArrayCharge(admissionC) + StringCharge(admissionOracle.Title) + StringCharge(admissionOracle.AuthorDbref));
		var expectedPeak = checked(initialCharge + expectedRetained - 128);
		void CapacityFailure(byte[] input, long limit)
		{
			try { WikiCategoryProjectionDecoder.Decode(input, limit)?.Dispose(); }
			catch (WikiCategoryProjectionCapacityException ex)
			{
				Check(ex.Dimension == "working-bytes", "Capacity dimension differs from pinned fixed literal");
				return;
			}
			throw new InvalidOperationException("Expected capacity failure");
		}
		Run("admission-independent-charge-model", () =>
		{
			using var projection = WikiCategoryProjectionDecoder.Decode(admissionBytes, 64L * 1024 * 1024)!;
			Console.WriteLine($"ADMISSION_MODEL source={sourceCharge} initial={initialCharge} retained={expectedRetained} peak={expectedPeak}");
			Check(projection.RetainedBytes == expectedRetained && projection.PeakReservedBytes == expectedPeak, "Reported charges differ from independently computed Codec-value model");
		});
		Run("admission-exact-peak", () =>
		{
			using var projection = WikiCategoryProjectionDecoder.Decode(admissionBytes, expectedPeak)!;
			Check(projection.Title == admissionOracle.Title && projection.AuthorDbref == admissionOracle.AuthorDbref && projection.CategoryCount == admissionCategories.Length, "Exact admission changed result");
		});
		foreach (var boundary in new[]
		{
			(Name: "initial-minus-one", Limit: initialCharge - 1, AllocationLimit: 8192L),
			(Name: "peak-minus-one", Limit: expectedPeak - 1, AllocationLimit: sourceCharge + 8192)
		})
			Run("admission-" + boundary.Name, () =>
			{
				for (var i = 0; i < 3; i++) CapacityFailure(admissionBytes, boundary.Limit);
				var before = GC.GetAllocatedBytesForCurrentThread();
				CapacityFailure(admissionBytes, boundary.Limit);
				var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
				Console.WriteLine($"ADMISSION_ALLOCATION {boundary.Name} allocated={allocated} independentLimit={boundary.AllocationLimit}");
				Check(allocated <= boundary.AllocationLimit, "EMPIRICAL ADMISSION ALLOCATION EXCESS: failure allocated above independently allowed scratch and exception allowance");
			});
		Run("capacity-precedes-malformed-preflight", () => CapacityFailure(Encoding.UTF8.GetBytes("truX_PRIVATE_SYNTHETIC"), 0));
		Run("dispose-full-access-matrix", () =>
		{
			var projection = WikiCategoryProjectionDecoder.Decode(admissionBytes, expectedPeak)!;
			projection.Dispose();
			projection.Dispose();
			Expect<ObjectDisposedException>(() => { _ = projection.Title; });
			Expect<ObjectDisposedException>(() => { _ = projection.AuthorDbref; });
			Expect<ObjectDisposedException>(() => { _ = projection.Published; });
			Expect<ObjectDisposedException>(() => { _ = projection.CategoriesAreNull; });
			Expect<ObjectDisposedException>(() => { _ = projection.CategoryCount; });
			Expect<ObjectDisposedException>(() => { _ = projection.DistinctCategoryCount; });
			Expect<ObjectDisposedException>(() => { _ = projection.IsNullCategory(0); });
			Expect<ObjectDisposedException>(() => { _ = projection.CategoryAt(0).Length; });
			Expect<ObjectDisposedException>(() => { _ = projection.DistinctCategoryOrdinalAt(0); });
			Expect<ObjectDisposedException>(() => { _ = projection.IsNullCategory(-1); });
			Expect<ObjectDisposedException>(() => { _ = projection.CategoryAt(-1).Length; });
			Expect<ObjectDisposedException>(() => { _ = projection.DistinctCategoryOrdinalAt(-1); });
		});
		Check(matched == 1, "Unknown auxiliary case: " + selected);
	}

	private static void Expect<T>(Action action) where T : Exception
	{
		try { action(); }
		catch (T) { return; }
		throw new InvalidOperationException("Expected " + typeof(T).Name);
	}

}
