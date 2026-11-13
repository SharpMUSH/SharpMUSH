using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Models;

/// <summary>
/// An attribute fetched to be run as a function: the object it was read from, its full name, and its
/// code. <see cref="Owner"/> is who the code runs as, and <see cref="Dialect"/> the dialect it is written in
/// (<see cref="Softcode.SoftcodeDialects"/>), or <c>null</c> for the game's.
/// </summary>
public readonly record struct AttributeFunction(AnySharpObject Owner, string Name, MString Code, string? Dialect = null)
{
	/// <summary>
	/// Who the code runs as when that is not <see cref="Owner"/>: the caller of a TinyMUX-style global
	/// <c>@function</c> (<c>mux_function_caller</c>). Null runs it as <see cref="Owner"/>.
	/// </summary>
	public AnySharpObject? RunAs { get; init; }

	/// <summary>
	/// Whether the attribute is set <c>noeval</c>: under <c>attribute_eval_flags</c>, running it gives <see cref="Code"/>
	/// as written.
	/// </summary>
	public bool NoEval { get; init; }

	/// <summary>
	/// The <c>#lambda</c> this is, when it is one. A lambda runs in its caller's frame with only its arguments
	/// and <c>%=</c> its own: the executor, caller and dialect stay the caller's.
	/// </summary>
	public LambdaEvaluation? Lambda { get; init; }

	/// <summary>
	/// The attribute's modification time (<see cref="SharpAttribute.ModifiedTime"/>), which with <see cref="Owner"/>
	/// and <see cref="Name"/> finds <see cref="Code"/>'s parse again. Null when it has none.
	/// </summary>
	public long? Modified { get; init; }

	/// <summary>
	/// The <c>#lambda</c> <paramref name="specifier"/> names (<c>#lambda/&lt;code&gt;</c>, everything after the first
	/// <c>/</c> its code), run by <paramref name="executor"/> inside <paramref name="enclosing"/>; or
	/// <see cref="NotFound"/> when it names no <c>#lambda</c>. A list function resolves it once and runs it per item.
	/// </summary>
	public static Found<AttributeFunction> OfLambda(AnySharpObject executor, MString specifier, DBAttribute? enclosing)
	{
		var slash = specifier.IndexOf("/");
		var head = (slash < 0 ? specifier : specifier.Substring(0, slash)).ToPlainText();
		if (!head.StartsWith("#lambda", StringComparison.OrdinalIgnoreCase))
		{
			return new NotFound();
		}

		var code = slash < 0
			? MString.Empty
			: MString.Join(MString.Plain("/"), specifier.Split("/").Skip(1));
		return OfLambdaCode(executor, code, enclosing);
	}

	/// <summary>A <c>#lambda</c> whose code is <paramref name="code"/>, run by <paramref name="executor"/> inside <paramref name="enclosing"/>.</summary>
	public static AttributeFunction OfLambdaCode(AnySharpObject executor, MString code, DBAttribute? enclosing)
		=> new(executor, "#LAMBDA", code)
		{
			Lambda = new LambdaEvaluation(executor.Object().DBRef, code.ToPlainText(), DBAttribute.Scope(enclosing))
		};

	/// <summary><paramref name="attribute"/>, read from <paramref name="owner"/>, to be run as a function.</summary>
	public static AttributeFunction Of(AnySharpObject owner, SharpAttribute attribute)
		=> new(owner, attribute.LongName.ToUpperInvariant(), attribute.Value, Softcode.SoftcodeDialects.Of(attribute))
		{
			NoEval = Extensions.SharpAttributeExtensions.IsNoEval(attribute),
			Modified = attribute.ModifiedTime
		};
}
