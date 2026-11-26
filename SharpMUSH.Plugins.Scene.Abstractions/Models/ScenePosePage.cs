namespace SharpMUSH.Plugins.Scene.Models;

/// <summary>
/// One page of a scene's poses in chain order, read in a single storage transaction.
/// </summary>
/// <param name="Poses">The page's poses, deleted ones included (as <c>GetPosesAsync</c> returns them).</param>
/// <param name="Next">
/// The cursor to pass for the page after this one, or null when no pose follows. Opaque to callers: it names
/// the position after which the next page starts, so a pose added meanwhile at the end is still reached.
/// </param>
public sealed record ScenePosePage(IReadOnlyList<ScenePose> Poses, long? Next);
