using SharpMUSH.Implementation;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Substitutions
{
	public static partial class Substitutions
	{
		public static CallState ParseSimpleSubstitution(string symbol, Parser parser, SubstitutionSymbolContext context)
		{

			switch (context.GetText())
			{
				default: break;
			}
			throw new NotImplementedException();
		}

		public static CallState ParseComplexSubstitution(string symbol, Parser parser, ComplexSubstitutionSymbolContext context)
		{
			throw new NotImplementedException();
		}
	}
}
