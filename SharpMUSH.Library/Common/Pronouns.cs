namespace SharpMUSH.Library.Common;

/// <summary>The four pronoun forms, as %s/subj(), %o/obj(), %p/poss() and %a/aposs() give them.</summary>
public enum PronounCase
{
	Subjective,
	Objective,
	Possessive,
	AbsolutePossessive
}

/// <summary>
/// The pronoun an object's gender attribute (SEX by default) asks for.
/// </summary>
/// <remarks>
/// PennMUSH's <c>get_gender</c> (<c>src/funstr.c:65-92</c>) reads only the attribute's first letter:
/// T or P is plural, M is male, F or W is female, ignoring case, so <c>male</c>, <c>m</c> and
/// <c>woman</c> all work. Anything else, and no attribute at all, is PennMUSH's neuter "it"; SharpMUSH
/// says "they" there instead, the same words as plural.
/// </remarks>
public static class Pronouns
{
	private enum Gender
	{
		Unknown,
		Female,
		Male,
		Plural
	}

	/// <summary>The pronoun in <paramref name="pronounCase"/> for a gender attribute holding <paramref name="sex"/>.</summary>
	public static string For(string sex, PronounCase pronounCase)
		=> (GenderOf(sex), pronounCase) switch
		{
			(Gender.Male, PronounCase.Subjective) => "he",
			(Gender.Male, PronounCase.Objective) => "him",
			(Gender.Male, _) => "his",
			(Gender.Female, PronounCase.Subjective) => "she",
			(Gender.Female, PronounCase.AbsolutePossessive) => "hers",
			(Gender.Female, _) => "her",
			(_, PronounCase.Subjective) => "they",
			(_, PronounCase.Objective) => "them",
			(_, PronounCase.Possessive) => "their",
			_ => "theirs"
		};

	private static Gender GenderOf(string sex)
		=> sex.Length == 0
			? Gender.Unknown
			: char.ToUpperInvariant(sex[0]) switch
			{
				'T' or 'P' => Gender.Plural,
				'M' => Gender.Male,
				'F' or 'W' => Gender.Female,
				_ => Gender.Unknown
			};
}
