namespace SharpMUSH.Library.API;

/// <summary><c>POST api/asciitranslations</c>: a character and the ASCII text sent for it; empty text leaves it out.</summary>
public sealed record AsciiTranslationRequest(string Character, string Text);
