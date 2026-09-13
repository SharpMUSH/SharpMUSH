namespace SharpMUSH.Library.Markup;

/// <summary>Literal accent templates index UTF-16 code units; unsupported pairs remain unchanged.</summary>
public static class AccentTemplate
{
	public static string Apply(string text, string template)
		=> text.Length != template.Length ? text : string.Create(text.Length, (text, template), static (span, state) =>
		{
			for (var i = 0; i < span.Length; i++) span[i] = ApplyAccent(state.text[i], state.template[i]);
		});

	private static char ApplyAccent(char c, char template)
	{
		// Accent mappings based on pennfunc.md ACCENTS table
		return (template, c) switch
		{
			// Grave accent (`)
			('`', 'A') => 'À',
			('`', 'E') => 'È',
			('`', 'I') => 'Ì',
			('`', 'O') => 'Ò',
			('`', 'U') => 'Ù',
			('`', 'a') => 'à',
			('`', 'e') => 'è',
			('`', 'i') => 'ì',
			('`', 'o') => 'ò',
			('`', 'u') => 'ù',

			// Acute accent (')
			('\'', 'A') => 'Á',
			('\'', 'E') => 'É',
			('\'', 'I') => 'Í',
			('\'', 'O') => 'Ó',
			('\'', 'U') => 'Ú',
			('\'', 'Y') => 'Ý',
			('\'', 'a') => 'á',
			('\'', 'e') => 'é',
			('\'', 'i') => 'í',
			('\'', 'o') => 'ó',
			('\'', 'u') => 'ú',
			('\'', 'y') => 'ý',

			// Tilde (~)
			('~', 'A') => 'Ã',
			('~', 'N') => 'Ñ',
			('~', 'O') => 'Õ',
			('~', 'a') => 'ã',
			('~', 'n') => 'ñ',
			('~', 'o') => 'õ',

			// Circumflex (^)
			('^', 'A') => 'Â',
			('^', 'E') => 'Ê',
			('^', 'I') => 'Î',
			('^', 'O') => 'Ô',
			('^', 'U') => 'Û',
			('^', 'a') => 'â',
			('^', 'e') => 'ê',
			('^', 'i') => 'î',
			('^', 'o') => 'ô',
			('^', 'u') => 'û',

			// Umlaut/Diaeresis (:)
			(':', 'A') => 'Ä',
			(':', 'E') => 'Ë',
			(':', 'I') => 'Ï',
			(':', 'O') => 'Ö',
			(':', 'U') => 'Ü',
			(':', 'a') => 'ä',
			(':', 'e') => 'ë',
			(':', 'i') => 'ï',
			(':', 'o') => 'ö',
			(':', 'u') => 'ü',
			(':', 'y') => 'ÿ',

			// Ring (o)
			('o', 'A') => 'Å',
			('o', 'a') => 'å',

			// Cedilla (,)
			(',', 'C') => 'Ç',
			(',', 'c') => 'ç',

			// Special characters
			('u', '?') => '¿',
			('u', '!') => '¡',
			('"', '<') => '«',
			('"', '>') => '»',
			('B', 's') => 'ß',
			('|', 'P') => 'Þ',
			('|', 'p') => 'þ',
			('-', 'D') => 'Ð',
			('&', 'o') => 'ð',

			_ => c
		};
	}
}

