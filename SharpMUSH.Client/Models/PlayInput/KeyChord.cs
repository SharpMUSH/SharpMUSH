namespace SharpMUSH.Client.Models.PlayInput;

/// <summary>
/// A key Play can bind to a command line, written as its modifiers then the key's code: <c>F2</c>,
/// <c>Shift+F2</c>, <c>Ctrl+Alt+Numpad8</c>. The code names the key's place on the keyboard (<c>KeyboardEvent.code</c>),
/// not the character it types, so a binding holds whatever the keyboard layout or Num Lock.
/// </summary>
/// <remarks>
/// Only function keys and the number pad can be bound. Letters, digits and punctuation type text, and the browser
/// and the portal keep their own: F5 reloads, F6 moves between panes, F11 is full screen, F12 opens the developer
/// tools, Ctrl+F4 and Alt+F4 close the tab or window, Ctrl with the number pad's plus and minus zooms and with its
/// digits is Ctrl and a digit, the number pad's Enter sends. Ctrl and a digit row number is kept for choosing a character, so a later page that
/// plays several at once does not take a player's keys away. The Meta (Command, Windows) key is the system's.
/// </remarks>
public static class KeyChord
{
	private static readonly string[] FunctionKeys = ["F1", "F2", "F3", "F4", "F7", "F8", "F9", "F10"];

	private static readonly string[] NumpadKeys =
	[
		"Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
		"NumpadDecimal", "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide",
	];

	/// <summary>The codes a chord may end in.</summary>
	public static IReadOnlyList<string> Codes { get; } = [.. FunctionKeys, .. NumpadKeys];

	/// <summary>The chord for a key press, in the order the modifiers are always written: Ctrl, Alt, Shift.</summary>
	public static string Of(string code, bool ctrl, bool alt, bool shift) =>
		(ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + code;

	/// <summary>Whether <paramref name="chord"/> is written as <see cref="Of"/> writes it and may be bound.</summary>
	public static bool Allowed(string chord)
	{
		var parts = chord.Split('+');
		var code = parts[^1];
		var modifiers = parts[..^1];
		if (!Codes.Contains(code)) return false;
		var ctrl = modifiers.Contains("Ctrl");
		var alt = modifiers.Contains("Alt");
		var shift = modifiers.Contains("Shift");
		if (Of(code, ctrl, alt, shift) != chord) return false;
		if (code == "F4" && (ctrl || alt)) return false;
		// Ctrl with a number pad digit is Ctrl and a digit to the browser (tab choice, zoom reset) with Num Lock on.
		return !(ctrl && (code is "NumpadAdd" or "NumpadSubtract" || code.Length == 7 && char.IsDigit(code[^1])));
	}
}
