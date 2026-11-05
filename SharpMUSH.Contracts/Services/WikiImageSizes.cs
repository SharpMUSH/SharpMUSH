using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services;

/// <summary>A picture's size in pixels.</summary>
public readonly record struct ImageSize(int Width, int Height);

/// <summary>
/// The size of a picture a wiki page shows, so the page can reserve its space before the picture arrives: a
/// page that does not shifts everything below each picture down as it loads.
/// </summary>
public interface IWikiImageSizes
{
	/// <summary>The size of the picture at <paramref name="url"/>; <c>NotFound</c> for a picture this game does not hold.</summary>
	Task<Found<ImageSize>> FindAsync(string url, CancellationToken ct = default);
}

/// <summary>Knows no picture's size: pages render as their Markdown sizes them.</summary>
public sealed class NoWikiImageSizes : IWikiImageSizes
{
	public static readonly NoWikiImageSizes Instance = new();

	public Task<Found<ImageSize>> FindAsync(string url, CancellationToken ct = default)
		=> Task.FromResult<Found<ImageSize>>(new NotFound());
}
