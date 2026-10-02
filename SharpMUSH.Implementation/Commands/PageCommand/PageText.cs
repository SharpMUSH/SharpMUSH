using SharpMUSH.Library.Common;

namespace SharpMUSH.Implementation.Commands.PageCommand;

/// <summary>
/// The default lines a page is shown as, before <c>PAGEFORMAT</c> / <c>OUTPAGEFORMAT</c>: what the
/// recipients read and what the pager reads (PennMUSH <c>do_page</c>, speech.c:1057-1130). <c>page</c>
/// delivers them, and <c>page/recall</c> shows a logged page as the same line.
/// </summary>
public static class PageText
{
	/// <summary>
	/// What a recipient reads: <c>Name pages: hi</c>, <c>Name pages A and B: hi</c>,
	/// <c>From afar, Name waves</c> and <c>From afar (to A and B), Name's …</c>.
	/// </summary>
	/// <param name="senderName">The pager as the page names them: with the page alias when
	/// <c>page_aliases</c> is on.</param>
	/// <param name="recipientNames">Everyone the page reached, in the order paged.</param>
	/// <param name="style"><c>say</c>, <c>pose</c> or <c>semipose</c>.</param>
	public static MString Incoming(string senderName, IReadOnlyList<string> recipientNames, string style,
		MString message)
	{
		var recipientList = MessageFormatting.FormatWithOxfordComma(recipientNames);
		var recipientSuffix = recipientNames.Count > 1 ? $" (to {recipientList})" : string.Empty;

		return style switch
		{
			"pose" => MarkupText.Concat([MarkupText.Plain($"From afar{recipientSuffix}, {senderName} "), message]),
			"semipose" => MarkupText.Concat([MarkupText.Plain($"From afar{recipientSuffix}, {senderName}"), message]),
			_ => MarkupText.Concat([
				MarkupText.Plain(recipientNames.Count > 1
					? $"{senderName} pages {recipientList}: "
					: $"{senderName} pages: "),
				message
			])
		};
	}

	/// <summary>
	/// What the pager reads: <c>You paged A with 'hi'</c>, <c>Long distance to A: Name waves</c>.
	/// </summary>
	/// <param name="senderOwnName">The pager's own name, without the page alias.</param>
	public static MString Outgoing(string senderOwnName, IReadOnlyList<string> recipientNames, string style,
		MString message)
	{
		var recipientList = MessageFormatting.FormatWithOxfordComma(recipientNames);

		return style switch
		{
			"pose" => MarkupText.Concat([MarkupText.Plain($"Long distance to {recipientList}: {senderOwnName} "), message]),
			"semipose" => MarkupText.Concat([MarkupText.Plain($"Long distance to {recipientList}: {senderOwnName}"), message]),
			_ => MarkupText.Concat([MarkupText.Plain($"You paged {recipientList} with '"), message, MarkupText.Plain("'")])
		};
	}
}
