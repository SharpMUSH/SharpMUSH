namespace SharpMUSH.Client.Models;

/// <summary>What the wiki editor publishes: the edited article, and the summary of the revision it makes (null when none was typed).</summary>
public record WikiEditSave(WikiArticle Article, string? EditSummary);
