namespace MarkupString.Ansi;

/// <summary>Installs this package's emitters and codec into a <see cref="MarkupRegistry"/>.</summary>
public static class AnsiRegistration
{
	/// <summary>
	/// Returns a registry that renders <see cref="AnsiMarkup"/> in all six formats and serialises
	/// it under kind <c>"ansi"</c>. Plain needs nothing: the body passes through.
	/// </summary>
	/// <remarks>
	/// The five emitters are <see cref="IMarkupSetEmitter"/>s — one per format, claiming the whole
	/// run — because the layers of a run have to fold into a single sequence or element. Layers
	/// this package does not own are delegated to their own emitters, so composing this with
	/// another package's registration works in either order.
	/// </remarks>
	public static MarkupRegistry WithAnsi(this MarkupRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		return registry
			.With(new AnsiSetEmitter())
			.With(new AnsiHtmlEmitter())
			.With(new AnsiPuebloEmitter())
			.With(new AnsiMxpEmitter())
			.With(new AnsiBBCodeEmitter())
			.With(new AnsiMarkupCodec());
	}
}
