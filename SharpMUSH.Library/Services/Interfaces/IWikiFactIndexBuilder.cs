using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Builds the wiki's fact index in the background, a batch of pages per write, so a build never holds the writer for
/// long and resumes where it stopped after a restart.
/// </summary>
public interface IWikiFactIndexBuilder
{
	/// <summary>Pages one batch indexes, or index entries one batch clears, in one write.</summary>
	const int BatchSize = 256;

	/// <summary>Starts building, or leaves the build already running alone. It ends when the index is ready.</summary>
	void Start();

	/// <summary>Starts the index over and builds it again.</summary>
	Task<WikiFactIndexStatus> RebuildAsync(CancellationToken cancellationToken = default);

	/// <summary>Stops the build after the batch it is writing; the next start resumes it.</summary>
	Task StopAsync();

	/// <summary>Ends when no build is running.</summary>
	Task Idle { get; }
}
