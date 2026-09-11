using SharpMUSH.Library.Services.DatabaseConversion;
using System.Diagnostics;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Performance tests for PennMUSH database converter.
/// These tests measure import performance with large databases.
/// </summary>
/// <remarks>
/// Each import runs in its own <see cref="IsolatedImportWorld"/>. A generated world is thousands of
/// objects that start in #0 and take over the seeded #0-#2, which is where every other suite in the
/// shared session world runs.
/// </remarks>
public class PennMUSHDatabaseConverterPerformanceTests
{

	/// <summary>
	/// Tests conversion performance with a large 10MB+ PennMUSH database.
	/// </summary>
	/// <remarks>
	/// Ten megabytes is about 540 objects and 29,000 attributes, and attributes are nearly all of the
	/// cost: Lightning converts it in about 12 seconds, SurrealDB in about 106. Each budget is roughly
	/// three to five times that.
	/// </remarks>
	[Test]
	[Category("Performance")]
	[Category("LongRunning")]
	public async ValueTask LargeDatabaseConversionPerformance()
	{
		var databaseFilePath = await PennMUSHDatabaseGenerator.GenerateLargeDatabaseFileAsync(10 * 1024 * 1024);

		try
		{
			await using var world = await IsolatedImportWorld.CreateAsync();
			var parser = world.Parser;
			var converter = world.Converter;

			var parseStopwatch = Stopwatch.StartNew();
			var database = await parser.ParseFileAsync(databaseFilePath);
			parseStopwatch.Stop();

			var convertStopwatch = Stopwatch.StartNew();
			var result = await converter.ConvertDatabaseAsync(database);
			convertStopwatch.Stop();

			await Assert.That(result.IsSuccessful).IsTrue();
			await Assert.That(result.TotalObjects).IsGreaterThan(0);

			var fileSize = new FileInfo(databaseFilePath).Length;
			var fileSizeMB = fileSize / (1024.0 * 1024.0);

			TestDiagnostics.WriteLine($"=== Performance Metrics ===");
			TestDiagnostics.WriteLine($"Database file size: {fileSizeMB:F2} MB");
			TestDiagnostics.WriteLine($"Total objects: {result.TotalObjects}");
			TestDiagnostics.WriteLine($"Players: {result.PlayersConverted}");
			TestDiagnostics.WriteLine($"Rooms: {result.RoomsConverted}");
			TestDiagnostics.WriteLine($"Things: {result.ThingsConverted}");
			TestDiagnostics.WriteLine($"Exits: {result.ExitsConverted}");
			TestDiagnostics.WriteLine($"Attributes: {result.AttributesConverted}");
			TestDiagnostics.WriteLine($"Locks: {result.LocksConverted}");
			TestDiagnostics.WriteLine($"Parse time: {parseStopwatch.Elapsed.TotalSeconds:F3} seconds");
			TestDiagnostics.WriteLine($"Convert time: {convertStopwatch.Elapsed.TotalSeconds:F3} seconds");
			TestDiagnostics.WriteLine($"Total time: {(parseStopwatch.Elapsed + convertStopwatch.Elapsed).TotalSeconds:F3} seconds");
			TestDiagnostics.WriteLine($"Objects/second: {result.TotalObjects / (parseStopwatch.Elapsed + convertStopwatch.Elapsed).TotalSeconds:F2}");
			TestDiagnostics.WriteLine($"MB/second: {fileSizeMB / (parseStopwatch.Elapsed + convertStopwatch.Elapsed).TotalSeconds:F2}");
			TestDiagnostics.WriteLine($"===========================");

			var dbProvider = Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER") ?? "";
			var isSurrealDb = dbProvider.Equals("surrealdb", StringComparison.OrdinalIgnoreCase);
			var timeoutSeconds = isSurrealDb ? 360.0 : 60.0;

			var totalTime = parseStopwatch.Elapsed + convertStopwatch.Elapsed;
			await Assert.That(totalTime.TotalSeconds).IsLessThan(timeoutSeconds)
				.Because($"Conversion of {fileSizeMB:F2}MB should complete in under {timeoutSeconds} seconds");
		}
		finally
		{
			if (File.Exists(databaseFilePath))
			{
				File.Delete(databaseFilePath);
			}
		}
	}

	/// <summary>
	/// Tests conversion performance with a database containing exactly 1000 objects.
	/// Useful for consistent benchmarking across test runs.
	/// </summary>
	[Test]
	[Category("Performance")]
	[Explicit("Performance test - run manually for benchmarking")]
	public async ValueTask FixedSizeDatabaseConversionPerformance()
	{
		var databaseFilePath = await PennMUSHDatabaseGenerator.GenerateDatabaseWithObjectCountAsync(1000);

		try
		{
			await using var world = await IsolatedImportWorld.CreateAsync();
			var parser = world.Parser;
			var converter = world.Converter;

			var stopwatch = Stopwatch.StartNew();
			var database = await parser.ParseFileAsync(databaseFilePath);
			var result = await converter.ConvertDatabaseAsync(database);
			stopwatch.Stop();

			await Assert.That(result.IsSuccessful).IsTrue();
			await Assert.That(result.TotalObjects).IsEqualTo(1000);

			TestDiagnostics.WriteLine($"1000-object conversion completed in {stopwatch.Elapsed.TotalSeconds:F3} seconds");
			TestDiagnostics.WriteLine($"  - Players: {result.PlayersConverted}");
			TestDiagnostics.WriteLine($"  - Rooms: {result.RoomsConverted}");
			TestDiagnostics.WriteLine($"  - Things: {result.ThingsConverted}");
			TestDiagnostics.WriteLine($"  - Exits: {result.ExitsConverted}");
			TestDiagnostics.WriteLine($"  - Attributes: {result.AttributesConverted}");
			TestDiagnostics.WriteLine($"  - Locks: {result.LocksConverted}");
			TestDiagnostics.WriteLine($"  - Objects/sec: {1000 / stopwatch.Elapsed.TotalSeconds:F2}");

			// About 55,000 attributes: some 22 seconds under Lightning, and roughly nine times that under
			// SurrealDB, as with the large database.
			var isSurrealDb = string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"),
				"surrealdb", StringComparison.OrdinalIgnoreCase);
			var budgetSeconds = isSurrealDb ? 600.0 : 60.0;
			await Assert.That(stopwatch.Elapsed.TotalSeconds).IsLessThan(budgetSeconds)
				.Because($"1000 objects should convert in under {budgetSeconds} seconds");
		}
		finally
		{
			if (File.Exists(databaseFilePath))
			{
				File.Delete(databaseFilePath);
			}
		}
	}

	/// <summary>
	/// Tests converter scaling with various database sizes.
	/// Measures performance across different object counts.
	/// </summary>
	[Test]
	[Category("Performance")]
	[Category("LongRunning")]
	[Explicit("Performance test - run manually for benchmarking")]
	public async ValueTask ScalabilityTest()
	{
		var objectCounts = new[] { 100, 500, 1000, 2000, 5000 };
		var results = new List<(int Objects, double Seconds, double ObjectsPerSecond)>();

		foreach (var count in objectCounts)
		{
			var databaseFilePath = await PennMUSHDatabaseGenerator.GenerateDatabaseWithObjectCountAsync(count);

			try
			{
				await using var world = await IsolatedImportWorld.CreateAsync();
				var parser = world.Parser;
				var converter = world.Converter;

				var stopwatch = Stopwatch.StartNew();
				var database = await parser.ParseFileAsync(databaseFilePath);
				var result = await converter.ConvertDatabaseAsync(database);
				stopwatch.Stop();

				await Assert.That(result.IsSuccessful).IsTrue();
				await Assert.That(result.TotalObjects).IsEqualTo(count);

				var objPerSec = count / stopwatch.Elapsed.TotalSeconds;
				results.Add((count, stopwatch.Elapsed.TotalSeconds, objPerSec));

				TestDiagnostics.WriteLine($"{count} objects: {stopwatch.Elapsed.TotalSeconds:F3}s ({objPerSec:F2} obj/s)");
			}
			finally
			{
				if (File.Exists(databaseFilePath))
				{
					File.Delete(databaseFilePath);
				}
			}
		}

		TestDiagnostics.WriteLine("\n=== Scalability Summary ===");
		TestDiagnostics.WriteLine("Objects | Time (s) | Obj/s");
		TestDiagnostics.WriteLine("--------|----------|-------");
		foreach (var (objects, seconds, objPerSec) in results)
		{
			TestDiagnostics.WriteLine($"{objects,7} | {seconds,8:F3} | {objPerSec,6:F2}");
		}

		var time1000 = results.First(r => r.Objects == 1000).Seconds;
		var time5000 = results.First(r => r.Objects == 5000).Seconds;
		var scalingRatio = time5000 / time1000;

		await Assert.That(scalingRatio).IsLessThan(6.0)
			.Because($"5000 objects should not take more than 6x the time of 1000 objects (actual: {scalingRatio:F2}x)");
	}
}
