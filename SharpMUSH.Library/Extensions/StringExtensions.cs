using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Extensions;

public static class StringExtensions
{
		/// <summary>
		/// TODO: Turn into a proper method of MModule instead.
		/// </summary>
		/// <param name="str">Glob Pattern</param>
		/// <returns>Regex Pattern</returns>
		public static string GlobToRegex(this string str)
			=> Regex.Escape(str)
				.Replace("\\*", ".*")
				.Replace("\\?", ".");
}