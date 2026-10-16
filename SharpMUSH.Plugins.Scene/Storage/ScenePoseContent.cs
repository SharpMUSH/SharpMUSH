namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// The two columns every pose edit keeps: the markup exactly as it was written, and the plain
/// projection of it.
///
/// <para>The command layer sends serialised markup, so the input is parsed as such; wrapping it in
/// <c>MarkupText.Plain</c> would treat it as literal characters and destroy real markup.</para>
///
/// <para>Legacy bare-text submissions are serialized once. Stored malformed markup is refused
/// before any edit, so a reader's display fallback never replaces the persisted source.</para>
/// </summary>
internal static class ScenePoseContent
{
	public static bool CanEdit(string markup)
	{
		if (markup.Length == 0) return true; // Legacy plain-text rows have no serialized projection.
		try { return MarkupTextSerializer.Inspect(markup).CanRewrite; }
		catch (System.Text.Json.JsonException) { return false; }
	}

	public static (string Plain, string Markup) Split(string? content)
	{
		if (string.IsNullOrEmpty(content))
		{
			return (string.Empty, string.Empty);
		}

		try
		{
			return (MarkupTextSerializer.Deserialize(content).ToPlainText(), content);
		}
		catch (System.Text.Json.JsonException)
		{
			// Not serialised markup — bare text, which is what every caller sent before the command
			// layer began preserving it. Its plain form is itself.
			return (content, MarkupTextSerializer.Serialize(MarkupText.Plain(content)));
		}
	}
}
