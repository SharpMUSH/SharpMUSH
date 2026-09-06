namespace MarkupString.Html;

/// <summary>Installs this package's emitters and codec into a <see cref="MarkupRegistry"/>.</summary>
public static class HtmlRegistration
{
	/// <summary>
	/// Returns a registry that renders <see cref="HtmlMarkup"/> as its own tag in Html, Pueblo and
	/// Mxp, and serialises it under kind <c>"html"</c>. Plain and BBCode need no emitter here — Plain
	/// passes the body through untouched, and BBCode's formatting for the handful of tags
	/// <see cref="HtmlMarkup"/> understands (bold, italic, underline, strikethrough) comes from the
	/// Ansi package's fold via <see cref="HtmlMarkup.TryGetAnsiStyle"/>, which requires
	/// <c>WithAnsi()</c> to already be applied.
	/// </summary>
	public static MarkupRegistry WithHtml(this MarkupRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		return registry
			.With(new HtmlTagEmitter(MarkupFormat.Html))
			.With(new HtmlTagEmitter(MarkupFormat.Pueblo))
			.With(new HtmlTagEmitter(MarkupFormat.Mxp))
			.With(new HtmlMarkupCodec());
	}
}
