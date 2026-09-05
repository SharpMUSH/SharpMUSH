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
	public static string[] SplitFields(MString arg, int count)
	{
		var text = arg.ToPlainText();
		var parts = text.Split(',', count, StringSplitOptions.None);
		var result = new string[count];
		for (var i = 0; i < count; i++)
		{
			result[i] = i < parts.Length ? parts[i].Trim() : string.Empty;
		}

		// The last field (content) preserves leading/trailing internal spacing but we
		// only trim the boundaries above; commas inside it were already kept by the limit.
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
	/// </summary>
	public static (string[] Fields, MString Content) SplitFieldsKeepingMarkup(MString arg, int count)
	{
		var fields = SplitFields(arg, count);
		var plain = arg.ToPlainText();

		// Walk to the character after the (count-1)th comma: where the last field starts.
		var start = 0;
		for (var comma = 0; comma < count - 1; comma++)
		{
			var next = plain.IndexOf(',', start);
			if (next < 0)
			{
				return (fields, MModule.empty());
			}

			start = next + 1;
		}

		// Match SplitFields' boundary trim, so the two views agree on where the content begins and
		// ends. Done by index rather than by trimming the MString, which would drop the markup again.
		var end = plain.Length;
		while (start < end && char.IsWhiteSpace(plain[start])) start++;
		while (end > start && char.IsWhiteSpace(plain[end - 1])) end--;

		return (fields, MModule.substring(start, end - start, arg));
	}

	/// <summary>Plain-text, trimmed view of an optional argument (null/empty → "").</summary>
	public static string Plain(MString? arg) => (arg?.ToPlainText() ?? string.Empty).Trim();
}
