namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// An asset opened for reading: its metadata and the stream of its bytes, which the caller owns and
/// must dispose.
/// </summary>
public readonly record struct OpenedWikiAsset(WikiAsset Asset, Stream Content);
