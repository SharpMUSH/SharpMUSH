namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// The two columns every pose edit keeps: the markup exactly as it was written, and the plain
/// projection of it.
///
/// <para>Each provider used to do this its own way and two of them were wrong. Arango wrote the same
/// string into both, so <c>markup</c> was whatever <c>content</c> was. Memgraph ran the incoming text
/// through <c>MModule.single</c>, which treats it as literal characters — fine while callers passed
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
			return (MModule.plainText(MModule.deserialize(content)), content);
		}
		catch
		{
			// Not serialised markup — bare text, which is what every caller sent before the command
			// layer began preserving it. Its plain form is itself.
			return (content, MModule.serialize(MModule.single(content)));
		}
	}
}
