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
	/// A pose as the softcode that writes it, <c>decompose()</c>'s answer: what the Edit box starts from, so
	/// saving it unchanged keeps every colour. <paramref name="content"/>, the plain text, stands in when
	/// <paramref name="markup"/> is not a serialized MString envelope.
	/// </summary>
	public static string ToSoftcode(string? markup, string content)
	{
		MarkupText text;
		try
		{
			text = string.IsNullOrEmpty(markup) ? MarkupText.Plain(content) : MarkupTextSerializer.Deserialize(markup);
		}
		catch (Exception)
		{
			text = MarkupText.Plain(content);
		}
		return SoftcodeDecomposer.Decompose(text);
	}
}
