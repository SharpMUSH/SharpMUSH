using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.ParserInterfaces;

public record DBAttribute(DBRef DB, string Name)
{
	public override string ToString()
		=> $"#{DB}/{Name.ToUpper()}";

	/// <summary>
	/// The evaluation that <c>##</c>, <c>#@</c>, <c>#$</c> and regexp captures bind by: the attribute itself, or for a
	/// <c>#lambda</c> the attribute around it. PennMUSH splices those tokens into the text before a lambda runs, so a
	/// lambda is transparent to them.
	/// </summary>
	public static DBAttribute? Scope(DBAttribute? evaluation)
		=> evaluation is LambdaEvaluation lambda ? lambda.Enclosing : evaluation;
}

/// <summary>
/// A <c>#lambda</c> being evaluated. PennMUSH's <c>call_ufun</c> runs it as an attribute named
/// <c>#LAMBDA/&lt;code&gt;</c>, which is what <c>%=</c> gives inside it.
/// </summary>
/// <param name="DB">The executor, which the lambda runs on.</param>
/// <param name="Code">The lambda's code, without markup, as <c>u()</c> evaluated it.</param>
/// <param name="Enclosing">The attribute around the lambda (its <see cref="DBAttribute.Scope"/>), or null.</param>
public sealed record LambdaEvaluation(DBRef DB, string Code, DBAttribute? Enclosing) : DBAttribute(DB, "#LAMBDA")
{
	public override string ToString() => $"#LAMBDA/{Code}";
}
