using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntlrCSharp.Implementation.Definitions
{
	public static class Predicates
	{
		private static bool Truthy(string text)
			=> !string.IsNullOrEmpty(text) && !text.StartsWith("#-") && text is not "0";

		private static bool Falsey(string text)
			=> !Truthy(text);
	}
}
