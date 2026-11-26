using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Runs the player's highlight rules (<see cref="HighlightRules"/>) over the play terminal's lines: gives every
/// server line a rule matches its look, marks and rewrites (<see cref="HighlightHtml"/>), hides it when a rule
/// says to, and plays the sound or raises the notification once per frame.
/// </summary>
/// <remarks>
/// <para>The look is applied to every line that comes through the terminal, the screen a reload restores
/// included, so a highlight survives a reload. An alert fires only for a line from a numbered frame the session
/// has not alerted for before (<see cref="TerminalResumeSlot.TryClaimAlert"/>): a restored screen carries no
/// frame number, a replay after a reconnect or a reload skips the frames already claimed, and earlier lines
/// brought back from the kept log come through as not live (<see cref="ITerminalService.MarkEarlier"/>).</para>
/// <para>One frame can hold several lines; the first that alerts claims the frame, so a frame alerts once.</para>
/// </remarks>
/// <param name="rules">Resolved on the first server line, so building the terminal does not build the account's services.</param>
public sealed class TerminalHighlights(Lazy<IHighlightMatcherSource> rules, Lazy<TerminalAlerts> alerts)
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
		var matcher = rules.Value.Matcher;
		if (matcher.IsEmpty || matcher.Match(line.Text, line.Comm) is not HighlightHit hit) return;

		line.Highlight = hit.Line;
		line.Hidden = hit.Hide;
		// A line whose shown text is not its plain text (a picture, a drawn table) takes no marks: none can be placed.
		if (!hit.KeepsText && HighlightHtml.Apply(line.Html, line.Text, hit) is string marked) line.MarkedHtml = marked;
		if (!live || line.Seq <= 0 || (hit.Sound == HighlightSound.None && !hit.Notify) || !Claim(line.Seq, slot)) return;
		_ = alerts.Value.AlertAsync(hit, line.Text, who);
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
