namespace SharpMUSH.Client.Models;

/// <summary>Why a translation save failed, and whether the fix is a reload rather than a correction.</summary>
/// <param name="Message">Server-supplied text, safe to show.</param>
/// <param name="NeedsReload">
/// True when the server answered 409: somebody else saved first. The editor must offer to reload and must
/// <b>not</b> retry — a retry re-sends this editor's stale markdown over the winner's.
/// </param>
public record WikiTranslationSaveError(string Message, bool NeedsReload);
