namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// The two columns every pose edit keeps: the markup exactly as it was written, and the plain
/// projection of it.
///
/// through <c>MarkupText.Plain</c>, which treats it as literal characters — fine while callers passed
/// bare text, and destructive the moment one passed real markup. Only SurrealDB read the input as
/// serialised markup, which is what the command layer now always sends.</para>
///
/// <para>The fallback arm matters as much as the happy one: a caller that passes bare text still gets
/// a valid serialised MString in <c>markup</c>, so a reader never has to guess which of the two
/// shapes a row is in.</para>
/// </summary>
internal static class ScenePoseContent
{
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
		catch
		{
			// Not serialised markup — bare text, which is what every caller sent before the command
			// layer began preserving it. Its plain form is itself.
			return (content, MarkupTextSerializer.Serialize(MarkupText.Plain(content)));
		}
	}
}
