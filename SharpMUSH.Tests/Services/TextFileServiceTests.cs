using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;

namespace SharpMUSH.Tests.Services;

public class TextFileServiceTests
{
	// SharpMUSHOptions is a record with many required properties, so load the checked-in minimal fixture
	// and override only what matters here — the same pattern as SitelockGuardTests.
	private static readonly SharpMUSHOptions BaseConfig =
		ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");

	/// <summary>
	/// A service rooted at a throwaway directory holding <paramref name="categories"/> categories of one
	/// markdown file each, so a reindex takes long enough for a concurrent reader to land inside it.
	/// </summary>
	private static (TextFileService Service, string Root) BuildServiceOverTempFiles(
		int categories, int entriesPerCategory)
	{
		var root = Path.Combine(Path.GetTempPath(), $"sharpmush-textfiles-{Guid.NewGuid():N}");
		for (var c = 0; c < categories; c++)
		{
			var dir = Path.Combine(root, $"cat{c}");
			Directory.CreateDirectory(dir);
			var body = string.Join("\n", Enumerable.Range(0, entriesPerCategory)
				.Select(e => $"# CAT{c}ENTRY{e}\n\nBody of entry {e} in category {c}.\n"));
			File.WriteAllText(Path.Combine(dir, "entries.md"), body);
		}

		var options = BaseConfig with
		{
			TextFile = BaseConfig.TextFile with { TextFilesDirectory = root, CacheOnStartup = false }
		};

		return (new TextFileService(Options.Create(options), NullLogger<TextFileService>.Instance), root);
	}

	/// <remarks>
	/// Regression: <c>ReindexAsync</c> used to clear the live index and refill it category by category, so
	/// for the duration of the file reads every concurrent <c>help</c> answered from a half-built index —
	/// usually an empty one. It surfaced as a rare unrelated test failure (<c>HelpCommandWorks</c> racing
	/// <c>@readcache</c>), which is how a production symptom looks when nothing asserts on it: on a live
	/// game, <c>@readcache</c> makes help transiently vanish for everyone.
	/// </remarks>
	[Test]
	public async Task ReindexAsync_NeverExposesAPartiallyBuiltIndex()
	{
		var (service, root) = BuildServiceOverTempFiles(categories: 8, entriesPerCategory: 40);
		try
		{
			await service.ReindexAsync();
			var expected = (await service.ListEntriesAsync(string.Empty)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
			await Assert.That(expected).IsEqualTo(8 * 40);

			using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			var reindexing = Task.Run(async () =>
			{
				while (!stop.IsCancellationRequested) await service.ReindexAsync();
			});

			var shortest = int.MaxValue;
			for (var i = 0; i < 400 && !stop.IsCancellationRequested; i++)
			{
				var seen = (await service.ListEntriesAsync(string.Empty)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
				shortest = Math.Min(shortest, seen);
			}

			await stop.CancelAsync();
			await reindexing;

			await Assert.That(shortest)
				.IsEqualTo(expected)
				.Because("a reader concurrent with a reindex must see the old index or the new one, never "
					+ "a prefix of the new one");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public async Task StripConsecutiveHeaders_SingleHeader_ReturnsUnchanged()
	{
		var content = "# FUNCTION LIST\n  Several major variants of functions are available.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo(content);
	}

	[Test]
	public async Task StripConsecutiveHeaders_TwoConsecutiveHeaders_KeepsOnlyFirst()
	{
		var content = "# FUNCTION LIST\n# FUNCTION TYPES\n  Several major variants of functions are available.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo("# FUNCTION LIST\n  Several major variants of functions are available.");
	}

	[Test]
	public async Task StripConsecutiveHeaders_ThreeConsecutiveHeaders_KeepsOnlyFirst()
	{
		var content = "# TOPIC1\n# TOPIC2\n# TOPIC3\n  Body text here.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo("# TOPIC1\n  Body text here.");
	}

	[Test]
	public async Task StripConsecutiveHeaders_NoHeaders_ReturnsUnchanged()
	{
		var content = "Just some plain text\nwith multiple lines.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo(content);
	}
}
