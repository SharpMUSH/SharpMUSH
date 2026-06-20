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

	/// <summary>Plain-text, trimmed view of an optional argument (null/empty → "").</summary>
	public static string Plain(MString? arg) => (arg?.ToPlainText() ?? string.Empty).Trim();
}
