namespace SharpMUSH.Client.Models;

/// <summary>
/// The prompt a terminal shows above its input line: the last frame the engine marked as a prompt.
/// </summary>
/// <param name="Line">The prompt as a server line; it reaches the scrollback only when the player answers it.</param>
/// <param name="Session">
/// The <c>@input</c> session it belongs to, or empty for a one-off <c>prompt()</c> or <c>@prompt</c>. A clear
/// naming another session leaves it alone.
/// </param>
public sealed record TerminalPrompt(TerminalLine Line, string Session);
