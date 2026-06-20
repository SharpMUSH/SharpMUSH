namespace SharpMUSH.Client.Services;

/// <summary>
/// Renders a pose's raw <c>Markup</c> (a serialized MString) to safe HTML on the
/// client, the same way the terminal does (MarkupString library →
/// <c>AnsiMarkup.WrapAsHtmlClass</c>). Poses carry raw markup over the wire; the
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
			return global::MarkupString.MarkupStringModule.deserialize(markup).Render("html");
		}
		catch (Exception)
		{
			return System.Net.WebUtility.HtmlEncode(markup);
		}
	}
}
