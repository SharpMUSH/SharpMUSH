namespace SharpMUSH.Library.Models.Scene;

/// <summary>
/// A single message line posted to a scene by a character.
/// Stores both the ANSI-rendered original (for live display in the terminal)
/// and the pre-rendered HTML (for web portal display and archiving).
/// </summary>
/// <param name="Id">Storage key. Empty string for unsaved messages.</param>
/// <param name="SceneId">The scene this message belongs to.</param>
/// <param name="AuthorDbref">DBRef string of the character who posted this line.</param>
/// <param name="AuthorName">Display name of the author at the time of posting.</param>
/// <param name="Content">Plain-text extraction of the message for search and accessibility.</param>
/// <param name="RenderedHtml">HTML representation of the MString content, pre-rendered via HtmlStrategy.</param>
/// <param name="Timestamp">UTC time when this message was posted.</param>
/// <param name="MessageType">Classifies the line (pose, say, system, OOC, etc.).</param>
public record SceneMessage(
	string Id,
	string SceneId,
	string AuthorDbref,
	string AuthorName,
	string Content,
	string RenderedHtml,
	DateTimeOffset Timestamp,
	SceneMessageType MessageType);
