namespace SharpMUSH.Implementation.Definitions
{
	public class Configurable
	{
		public const int MaxCallDepth = 1000000;
		public const int MaxFunctionDepth = 1000000;
		public const int MaxRecursionDepth = 1000000;

		public const int PlayerStart = 1;

		/// <summary>
		/// Historically, MU*s use the old SEX attribute for this for Compatibility reasons.
		/// </summary>
		public const string GenderAttribute = "GENDER";

		/// <summary>
		/// Overrides the behavior of %p. Expects the style: '#5/SUB`POSSESSIVE`PRONOUN'.
		/// The attribute will get evaluated and given %0 with the target, 
		/// and %1 with the value of the GenderAttribute.
		/// 
		/// Defaults to he/she/it/they as poss()
		/// </summary>
		public const string PossessivePronounFunction = "";

		/// <summary>
		/// Overrides the behavior of %a. Expects the style: '#5/SUB`ABS_POSSESSIVE`PRONOUN'.
		/// The attribute will get evaluated and given %0 with the target.
		/// 
		/// Defaults to his/hers/its/theirs as aposs()
		/// </summary>
		public const string AbsolutePossessivePronounFunction = "";


		/// <summary>
		/// Overrides the behavior of %o. Expects the style: '#5/SUB`OBJECTIVE`PRONOUN'.
		/// The attribute will get evaluated and given %0 with the target.
		/// 
		/// Defaults to him, her, it, them as obj()
		/// </summary>
		public const string ObjectivePronounFunction = "";

		/// <summary>
		/// Overrides the behavior of %s. Expects the style: '#5/SUB`SUBJECTIVE`PRONOUN'.
		/// The attribute will get evaluated and given %0 with the target.
		/// 
		/// Defaults to he, she, it, they as subj()
		/// </summary>
		public const string SubjectivePronounFunction = "";
	}
}
