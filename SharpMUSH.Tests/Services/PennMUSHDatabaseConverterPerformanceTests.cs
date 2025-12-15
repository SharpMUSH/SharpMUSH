using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.DatabaseConversion;
using TUnit.Core;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Performance tests for PennMUSH database converter.
/// These tests measure import performance with large databases.
/// </summary>
[ClassDataSource<WebAppFactory>(Shared = SharedType.Keyed, Key = "PennMUSHConversion")]
public class PennMUSHDatabaseConverterPerformanceTests(WebAppFactory webAppFactory)
{
	private IPennMUSHDatabaseConverter GetConverter()
	{
		return webAppFactory.Services.GetRequiredService<IPennMUSHDatabaseConverter>();
	}

	private PennMUSHDatabaseParser GetParser()
	{
		return webAppFactory.Services.GetRequiredService<PennMUSHDatabaseParser>();
	}

	/// <summary>
	/// Tests conversion performance with a large 10MB+ PennMUSH database.
	/// Runs 10 times to get consistent performance measurements.
	/// </summary>
	[Test]
	[Repeat(10)]
	[Category("Performance")]
	[Category("LongRunning")]
	public async ValueTask LargeDatabaseConversionPerformance()
	{
		// Generate a large fake database (10MB+)
		var databaseFilePath = await PennMUSHDatabaseGenerator.GenerateLargeDatabaseFileAsync(10 * 1024 * 1024);
		
		try
		{
			var parser = GetParser();
			var converter = GetConverter();

			// Parse the database file
			var parseStopwatch = Stopwatch.StartNew();
			var database = await parser.ParseFileAsync(databaseFilePath);
			parseStopwatch.Stop();

			// Convert the database
			var convertStopwatch = Stopwatch.StartNew();
			var result = await converter.ConvertDatabaseAsync(database);
			convertStopwatch.Stop();

			// Verify the conversion succeeded
			await Assert.That(result.IsSuccessful).IsTrue();
			await Assert.That(result.TotalObjects).IsGreaterThan(0);

			// Log performance metrics
			var fileSize = new FileInfo(databaseFilePath).Length;
			var fileSizeMB = fileSize / (1024.0 * 1024.0);
			
			Console.WriteLine($"=== Performance Metrics ===");
			Console.WriteLine($"Database file size: {fileSizeMB:F2} MB");
			Console.WriteLine($"Total objects: {result.TotalObjects}");
			Console.WriteLine($"Players: {result.PlayersConverted}");
			Console.WriteLine($"Rooms: {result.RoomsConverted}");
			Console.WriteLine($"Things: {result.ThingsConverted}");
			Console.WriteLine($"Exits: {result.ExitsConverted}");
			Console.WriteLine($"Attributes: {result.AttributesConverted}");
			Console.WriteLine($"Locks: {result.LocksConverted}");
			Console.WriteLine($"Parse time: {parseStopwatch.Elapsed.TotalSeconds:F3} seconds");
			Console.WriteLine($"Convert time: {convertStopwatch.Elapsed.TotalSeconds:F3} seconds");
			Console.WriteLine($"Total time: {(parseStopwatch.Elapsed + convertStopwatch.Elapsed).TotalSeconds:F3} seconds");
			Console.WriteLine($"Objects/second: {result.TotalObjects / (parseStopwatch.Elapsed + convertStopwatch.Elapsed).TotalSeconds:F2}");
			Console.WriteLine($"MB/second: {fileSizeMB / (parseStopwatch.Elapsed + convertStopwatch.Elapsed).TotalSeconds:F2}");
			Console.WriteLine($"===========================");

			// Performance assertions - should complete in reasonable time
			// For a 10MB database, we expect parsing + conversion to complete in under 60 seconds
			var totalTime = parseStopwatch.Elapsed + convertStopwatch.Elapsed;
			await Assert.That(totalTime.TotalSeconds).IsLessThan(60.0)
				.Because($"Conversion of {fileSizeMB:F2}MB should complete in under 60 seconds");
		}
		finally
		{
			// Cleanup: delete the temporary database file
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
	[Repeat(10)]
	[Category("Performance")]
	public async ValueTask FixedSizeDatabaseConversionPerformance()
	{
		// Generate a database with exactly 1000 objects
		var databaseFilePath = await PennMUSHDatabaseGenerator.GenerateDatabaseWithObjectCountAsync(1000);
		
		try
		{
			var parser = GetParser();
			var converter = GetConverter();

			// Parse and convert
			var stopwatch = Stopwatch.StartNew();
			var database = await parser.ParseFileAsync(databaseFilePath);
			var result = await converter.ConvertDatabaseAsync(database);
			stopwatch.Stop();

			// Verify
			await Assert.That(result.IsSuccessful).IsTrue();
			await Assert.That(result.TotalObjects).IsEqualTo(1000);

			// Log metrics
			Console.WriteLine($"1000-object conversion completed in {stopwatch.Elapsed.TotalSeconds:F3} seconds");
			Console.WriteLine($"  - Players: {result.PlayersConverted}");
			Console.WriteLine($"  - Rooms: {result.RoomsConverted}");
			Console.WriteLine($"  - Things: {result.ThingsConverted}");
			Console.WriteLine($"  - Exits: {result.ExitsConverted}");
			Console.WriteLine($"  - Attributes: {result.AttributesConverted}");
			Console.WriteLine($"  - Locks: {result.LocksConverted}");
			Console.WriteLine($"  - Objects/sec: {1000 / stopwatch.Elapsed.TotalSeconds:F2}");

			// Should complete in under 10 seconds for 1000 objects
			await Assert.That(stopwatch.Elapsed.TotalSeconds).IsLessThan(10.0)
				.Because("1000 objects should convert in under 10 seconds");
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
	public async ValueTask ScalabilityTest()
	{
		var objectCounts = new[] { 100, 500, 1000, 2000, 5000 };
		var results = new List<(int Objects, double Seconds, double ObjectsPerSecond)>();

		foreach (var count in objectCounts)
		{
			var databaseFilePath = await PennMUSHDatabaseGenerator.GenerateDatabaseWithObjectCountAsync(count);
			
			try
			{
				var parser = GetParser();
				var converter = GetConverter();

				var stopwatch = Stopwatch.StartNew();
				var database = await parser.ParseFileAsync(databaseFilePath);
				var result = await converter.ConvertDatabaseAsync(database);
				stopwatch.Stop();

				await Assert.That(result.IsSuccessful).IsTrue();
				await Assert.That(result.TotalObjects).IsEqualTo(count);

				var objPerSec = count / stopwatch.Elapsed.TotalSeconds;
				results.Add((count, stopwatch.Elapsed.TotalSeconds, objPerSec));

				Console.WriteLine($"{count} objects: {stopwatch.Elapsed.TotalSeconds:F3}s ({objPerSec:F2} obj/s)");
			}
			finally
			{
				if (File.Exists(databaseFilePath))
				{
					File.Delete(databaseFilePath);
				}
			}
		}

		// Display summary
		Console.WriteLine("\n=== Scalability Summary ===");
		Console.WriteLine("Objects | Time (s) | Obj/s");
		Console.WriteLine("--------|----------|-------");
		foreach (var (objects, seconds, objPerSec) in results)
		{
			Console.WriteLine($"{objects,7} | {seconds,8:F3} | {objPerSec,6:F2}");
		}

		// Verify reasonable scaling - larger databases should not be disproportionately slower
		// Check that 5000 objects doesn't take more than 6x the time of 1000 objects
		var time1000 = results.First(r => r.Objects == 1000).Seconds;
		var time5000 = results.First(r => r.Objects == 5000).Seconds;
		var scalingRatio = time5000 / time1000;
		
		await Assert.That(scalingRatio).IsLessThan(6.0)
			.Because($"5000 objects should not take more than 6x the time of 1000 objects (actual: {scalingRatio:F2}x)");
	}
}
