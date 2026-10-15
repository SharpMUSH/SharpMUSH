using System.Text.RegularExpressions;
using MarkupString;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Discord.Outbox;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>How a feed line reads in Discord: posted under the speaker's name, its text as Markdown.</summary>
public static partial class DiscordLineText
{
	/// <summary>Discord takes a webhook name of 1 to 80 characters.</summary>
	public const int MaxSpeaker = 80;

	/// <summary>The name a line goes out under when its speaker's name leaves nothing Discord accepts.</summary>
	public const string FallbackSpeaker = "Game";

	/// <summary>
	/// A pose reads as the game shows it, in italics: <c>*Alice waves.*</c>. A line the system formatted once for
	/// everyone (a channel line after its mogrifier) goes out as delivered.
	/// </summary>
	public static OutboundLine From(SharpFeedMessage message)
	{
		var name = string.IsNullOrWhiteSpace(message.DisplayName) ? message.SpeakerName : message.DisplayName;
		var text = message.Line is { } line
			? Markdown(line)
			: message.Style switch
			{
				FeedStyles.Pose => Italic($"{Markdown(MarkupText.Plain(name))} {Markdown(message.Text)}"),
				FeedStyles.SemiPose => Italic($"{Markdown(MarkupText.Plain(name))}{Markdown(message.Text)}"),
				_ => Markdown(message.Text),
			};
		return new OutboundLine(Speaker(name), null, text, message.At);
	}

	/// <summary>
	/// A webhook name Discord accepts: whitespace runs as one space, at most <see cref="MaxSpeaker"/> characters, and
	/// "discord" and "clyde", which Discord refuses in webhook names, broken by a zero-width space.
	/// </summary>
	public static string Speaker(string name)
	{
		var speaker = Whitespace().Replace(name, " ").Trim();
		speaker = Reserved().Replace(speaker, match => $"{match.Value[0]}​{match.Value[1..]}");
		if (speaker.Length > MaxSpeaker)
		{
			speaker = speaker[..MaxSpeaker].TrimEnd();
		}

		return speaker.Length == 0 ? FallbackSpeaker : speaker;
	}

	private static string Markdown(MarkupText text) => text.Render(MarkupFormat.Markdown).Trim();

	/// <summary>Markdown italics need no space just inside the asterisks, which <see cref="Markdown"/> has trimmed.</summary>
	private static string Italic(string text) => text.Length == 0 ? text : $"*{text}*";

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	[GeneratedRegex("discord|clyde", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex Reserved();
}
