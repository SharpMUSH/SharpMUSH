namespace SharpMUSH.Library.Webhooks;

/// <summary>
/// The patterns a webhook subscription chooses its events by: an event name, where <c>*</c> stands for
/// any run of characters (the <c>`</c> between a group and its event included) and <c>?</c> for one.
/// Case does not matter.
/// </summary>
public static class WebhookEventPattern
{
	/// <summary>Whether <paramref name="eventName"/> matches <paramref name="pattern"/>.</summary>
	public static bool Matches(string pattern, string eventName)
		=> Matches(pattern.AsSpan(), eventName.AsSpan());

	/// <summary>Whether <paramref name="pattern"/> could name an event: not empty, and no spaces.</summary>
	public static bool IsValid(string pattern)
		=> pattern.Length > 0 && !pattern.Any(char.IsWhiteSpace);

	private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
	{
		// The classic two-pointer glob: on a mismatch, go back to the last * and let it take one more character.
		int p = 0, t = 0, star = -1, resume = 0;
		while (t < text.Length)
		{
			if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
			{
				p++;
				t++;
			}
			else if (p < pattern.Length && pattern[p] == '*')
			{
				star = p++;
				resume = t;
			}
			else if (star >= 0)
			{
				p = star + 1;
				t = ++resume;
			}
			else
			{
				return false;
			}
		}

		while (p < pattern.Length && pattern[p] == '*') p++;
		return p == pattern.Length;
	}
}
