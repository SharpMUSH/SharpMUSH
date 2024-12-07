﻿namespace SharpMUSH.Implementation.Definitions;

public static class Predicates
{
		public static bool Truthy(MString text)
		{
				var plainText = MModule.plainText(text);
				return !string.IsNullOrEmpty(plainText) && !plainText.StartsWith("#-") && plainText is not "0";
		}

		public static bool Falsey(MString text)
			=> !Truthy(text);
}