namespace SharpMUSH.Client.Models;

/// <summary>
/// A single revision entry as shown in the history dialog.
/// <see cref="MarkdownSource"/> is the full page body at that revision,
/// used by the diff view.
/// </summary>
public record WikiRevisionInfo(
	int RevisionNumber,
	string EditorDbref,
	DateTimeOffset Timestamp,
	string? EditSummary,
	string MarkdownSource);
