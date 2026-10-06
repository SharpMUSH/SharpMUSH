using SharpMUSH.Library.Markup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Renders a pose's raw <c>Markup</c> (a serialized MString) to safe HTML on the
/// client, the same way the terminal does
/// (<c>MarkupText.Render(MarkupFormat.Html)</c>). Poses carry raw markup over the wire; the
/// portal renders it client-side and never trusts server-produced HTML. Falls back
/// to HTML-encoded plain text when the value is not a serialized MString envelope.
/// </summary>
public static class SceneMarkupRenderer
{
	public static string ToHtml(string? markup)
	{
		if (string.IsNullOrEmpty(markup))
		{
			return string.Empty;
		}

		try
		{
			return MarkupTextSerializer.Deserialize(markup).Render(MarkupFormat.Html);
		}
		catch (Exception)
		{
			return System.Net.WebUtility.HtmlEncode(markup);
		}
	}

	/// <summary>
	/// A pose as styled text, what the Edit box starts from: the serialized MString in <paramref name="markup"/>,
	/// or <paramref name="content"/>, the plain text, when that is not one.
	/// </summary>
	public static MarkupText ToMarkupText(string? markup, string content)
	{
		try
		{
			return string.IsNullOrEmpty(markup) ? MarkupText.Plain(content) : MarkupTextSerializer.Deserialize(markup);
		}
		catch (System.Text.Json.JsonException)
		{
			return MarkupText.Plain(content);
		}
	}

	/// <summary>
	/// A pose as the softcode that writes it, <c>decompose()</c>'s answer, so saving it unchanged keeps every
	/// colour.
	/// </summary>
	public static string ToSoftcode(string? markup, string content) => SoftcodeDecomposer.Decompose(ToMarkupText(markup, content));
}
