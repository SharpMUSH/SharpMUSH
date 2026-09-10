using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Shared helpers for the wizard-only @SCENE primitive surface: error strings,
/// dbref/id text extraction, and the comma-separated "content-last" argument
/// parsing the design table prescribes.
/// </summary>
public static class SceneCommandHelper
{
	public const string PermissionDeniedNotice = "SCENE: Permission denied.";
	public const string PermissionDeniedReturn = "#-1 PERMISSION DENIED";
	public const string BadArguments = "#-1 BAD ARGUMENTS TO SCENE COMMAND";
	public const string NotFound = "#-1 NO SUCH SCENE OR POSE";

	/// <summary>
	/// Splits a left-hand-side <c>&lt;id&gt;[/&lt;key&gt;]</c> reference into its id and an
	/// optional trailing key. Both are plain-text, trimmed.
	/// </summary>
	public static (string Id, string? Key) SplitIdKey(MString lhs)
	{
		var text = lhs.ToPlainText();
		var slash = text.IndexOf('/');
		return slash < 0
			? (text.Trim(), null)
			: (text[..slash].Trim(), text[(slash + 1)..].Trim());
	}

	/// <summary>
	/// Splits a comma-separated argument list into exactly <paramref name="count"/>
	/// fields, where the final field ("content") keeps any remaining commas intact.
	/// Missing trailing fields come back as empty strings.
	/// </summary>
	/// <param name="trimLast">
	/// Whether the final field is trimmed like the rest. True for the fields that are titles or
	/// descriptions, where a space after the comma is typing and not text. False for a pose's
	/// content, whose leading and trailing whitespace is the author's — see
	/// <see cref="SplitFieldsKeepingMarkup"/>.
	/// </param>
	public static string[] SplitFields(MString arg, int count, bool trimLast = true)
	{
		var text = arg.ToPlainText();
		var parts = text.Split(',', count, StringSplitOptions.None);
		var result = new string[count];
		for (var i = 0; i < count; i++)
		{
			var part = i < parts.Length ? parts[i] : string.Empty;
			result[i] = trimLast || i < count - 1 ? part.Trim() : part;
		}

		return result;
	}

	/// <summary>
	/// <see cref="SplitFields"/>, plus the final "content" field with its markup intact.
	///
	/// <para>The storage layer wants that field as a SERIALISED MString: it keeps what it is handed in
	/// <c>markup</c> and derives the plain <c>content</c> column from it. Handing it
	/// <see cref="SplitFields"/>' output could never satisfy that, because that method starts with
	/// <c>ToPlainText()</c> — so a pose written with <c>ansi()</c> arrived already flat, the
	/// deserialize fell through to its fallback, and <c>markup</c> ended up holding the same bare
	/// sentence as <c>content</c>. It rendered coloured in a terminal and grey on the web.</para>
	///
	/// <para>Only the last field is worth carrying: every earlier one is a dbref, a role or a keyword
	/// that is compared as text.</para>
	///
	/// <para>The content's own leading and trailing whitespace is kept. It is the author's: an
	/// indented pose, a line that opens on a blank one, a deliberate hanging break. Trimming it here
	/// silently undid the <c>%b</c>/<c>%r</c> the poser (or the portal's compose box) used to get that
	/// whitespace past the parser in the first place, so an indent could not be written at all.</para>
	/// </summary>
	public static (string[] Fields, MString Content) SplitFieldsKeepingMarkup(MString arg, int count)
	{
		var fields = SplitFields(arg, count, trimLast: false);
		var plain = arg.ToPlainText();

		// Walk to the character after the (count-1)th comma: where the last field starts.
		var start = 0;
		for (var comma = 0; comma < count - 1; comma++)
		{
			var next = plain.IndexOf(',', start);
			if (next < 0)
			{
				return (fields, MarkupText.Empty);
			}

			start = next + 1;
		}

		return (fields, arg.Substring(start, plain.Length - start));
	}

	/// <summary>Plain-text, trimmed view of an optional argument (null/empty → "").</summary>
	public static string Plain(MString? arg) => (arg?.ToPlainText() ?? string.Empty).Trim();
}
