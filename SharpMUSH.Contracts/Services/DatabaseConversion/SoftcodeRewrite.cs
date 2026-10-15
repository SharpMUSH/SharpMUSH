namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// One attribute whose softcode the import rewrote so SharpMUSH reads it as the source server did.
/// </summary>
/// <param name="Attribute">The object and attribute, as <c>#9/DESCRIBE</c>.</param>
/// <param name="Note">What was rewritten, as <c>%c/%x colour written as ansi()</c>; <c>check by hand</c> and the
/// reason where the rewrite could not carry the code over exactly.</param>
/// <param name="Before">The attribute's text as the source stored it.</param>
/// <param name="After">The text the import stored instead.</param>
public record SoftcodeRewrite(string Attribute, string Note, string Before, string After)
{
	/// <summary>Whether the rewrite could not carry some of the code over exactly.</summary>
	public bool ByHand => Note.Contains("check by hand", StringComparison.Ordinal);

	public override string ToString() => $"{Attribute}: {Note}";
}
