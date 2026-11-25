using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Runs the player's highlight rules (<see cref="HighlightRules"/>) over the play terminal's lines: marks the
/// colour on every server line a rule matches, and plays the sound or raises the notification once per frame.
/// </summary>
/// <remarks>
/// <para>Colour is applied to every line that comes through the terminal, the screen a reload restores
/// included, so a highlight survives a reload. An alert fires only for a line from a numbered frame the session
/// has not alerted for before (<see cref="TerminalResumeSlot.TryClaimAlert"/>): a restored screen carries no
/// frame number, a replay after a reconnect or a reload skips the frames already claimed, and earlier lines
/// loaded from the kept log never come through here at all.</para>
/// <para>One frame can hold several lines; the first that alerts claims the frame, so a frame alerts once.</para>
/// </remarks>
public sealed class TerminalHighlights(IHighlightMatcherSource rules, TerminalAlerts alerts)
{
	// For a terminal with no resume slot (a guest): the frame last alerted for, so one frame alerts once.
	private long _lastAlerted;

	/// <summary>Marks <paramref name="line"/> and alerts for it, as the rules say.</summary>
	/// <param name="live">The line just arrived from the server, rather than being shown again.</param>
	/// <param name="slot">Where the session keeps the frame it last alerted for, when it keeps one.</param>
	/// <param name="who">The character the terminal plays, for the notification's title.</param>
	public void Apply(TerminalLine line, bool live, TerminalResumeSlot? slot, string? who)
	{
		if (line.Source != TerminalLineSource.Server) return;
		var matcher = rules.Matcher;
		if (matcher.IsEmpty || matcher.Match(line.Text, line.Comm) is not HighlightHit hit) return;

		line.Highlight = hit.Colour;
		if (!live || line.Seq <= 0 || (hit.Sound == HighlightSound.None && !hit.Notify) || !Claim(line.Seq, slot)) return;
		_ = alerts.AlertAsync(hit, line.Text, who);
	}

	private bool Claim(long seq, TerminalResumeSlot? slot)
	{
		if (slot is { Revoked: false }) return slot.TryClaimAlert(seq);
		// A fresh session numbers from 1 again, so any frame but the one just alerted for is new.
		if (seq == _lastAlerted) return false;
		_lastAlerted = seq;
		return true;
	}
}
