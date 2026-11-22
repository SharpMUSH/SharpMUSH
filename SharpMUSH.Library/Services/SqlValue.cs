using System.Text;

namespace SharpMUSH.Library.Services;

/// <summary>A value a SQL row holds, as the text sql(), mapsql() and @sql give it.</summary>
public static class SqlValue
{
	private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>
	/// <paramref name="value"/> as text. A binary column (BLOB, BINARY, a function typed binary such as MariaDB's
	/// <c>TO_BASE64</c> on some versions) arrives as bytes, which PennMUSH hands back as the text they spell: they are
	/// read as UTF-8 where they are valid UTF-8 and as Latin-1 otherwise, never as <c>System.Byte[]</c>. A null is empty.
	/// </summary>
	public static string Text(object? value) => value switch
	{
		null or DBNull => string.Empty,
		byte[] bytes => Decode(bytes),
		_ => value.ToString() ?? string.Empty
	};

	private static string Decode(byte[] bytes)
	{
		try
		{
			return StrictUtf8.GetString(bytes);
		}
		catch (DecoderFallbackException)
		{
			return Encoding.Latin1.GetString(bytes);
		}
	}
}
