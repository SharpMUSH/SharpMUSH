using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The <c>dict_file</c> a word list was last read from, and its modification time (expanded server
/// data), so a restart reads the file again only when it changed.
/// </summary>
public sealed class DictionaryFileStamp
{
	public string? Path { get; set; }
	public long ModifiedTicks { get; set; }
}

/// <summary>
/// PennMUSH's <c>add_dict_words</c> (<c>src/help.c:1700</c>): at startup the <c>words</c> suggestion
/// category becomes the lower-cased lines of <c>dict_file</c>, one word per line. The file is read
/// only when it changed since the last read; an unset <c>dict_file</c> leaves the category alone.
/// </summary>
public class DictionaryWordsBootstrapService(
	IOptionsWrapper<SharpMUSHOptions> options,
	IExpandedObjectDataService serverData,
	ILogger<DictionaryWordsBootstrapService> logger) : IHostedService
{
	public const string WordsCategory = "words";

	/// <summary>Penn's BUFFER_LEN: a longer line is skipped rather than cut.</summary>
	private const int MaxLineLength = 8192;

	public Task StartAsync(CancellationToken cancellationToken)
		=> LoadAsync(options.CurrentValue.File.DictionaryFile, cancellationToken);

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>Replaces the <c>words</c> category with the words in <paramref name="path"/>, if it changed.</summary>
	public async Task LoadAsync(string? path, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		if (!File.Exists(path))
		{
			logger.LogWarning("Unable to read word list {DictionaryFile}: no such file.", path);
			return;
		}

		var modified = File.GetLastWriteTimeUtc(path).Ticks;
		if (await serverData.GetExpandedServerDataAsync<DictionaryFileStamp>() is { } stamp
				&& stamp.Path == path && stamp.ModifiedTicks == modified)
		{
			logger.LogInformation("Using cached copy of dict_file words.");
			return;
		}

		logger.LogInformation("Reading word list from {DictionaryFile}", path);
		var words = new HashSet<string>(StringComparer.Ordinal);
		await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
		{
			if (line.Length is > 0 and <= MaxLineLength)
			{
				words.Add(line.ToLowerInvariant());
			}
		}

		var suggestions = await serverData.GetExpandedServerDataAsync<SuggestionData>() ?? new SuggestionData();
		var categories = new Dictionary<string, HashSet<string>>(suggestions.Categories ?? [])
		{
			[WordsCategory] = words
		};
		await serverData.SetExpandedServerDataAsync(suggestions with { Categories = categories });
		await serverData.SetExpandedServerDataAsync(new DictionaryFileStamp { Path = path, ModifiedTicks = modified });
		logger.LogInformation("Done reading words: {Count} from {DictionaryFile}.", words.Count, path);
	}
}
