namespace MarkupString;

/// <summary>
/// A markup layer read from JSON whose <c>"k"</c> kind no registry codec claimed. It keeps the raw
/// object so the layer round-trips verbatim, and renders as nothing at all in every format.
/// </summary>
public sealed record UnknownMarkup(string Kind, string RawJson) : IMarkup;
