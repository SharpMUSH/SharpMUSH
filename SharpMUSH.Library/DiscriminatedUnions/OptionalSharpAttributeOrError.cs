using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpAttribute[], None, and SharpError.
/// Replaces OptionalSharpAttributeOrError : OneOfBase&lt;SharpAttribute[], None, Error&lt;string&gt;&gt;.
/// </summary>
public union OptionalSharpAttributeOrError(SharpAttribute[], None, SharpError)
{
	public bool IsAttribute => Value is SharpAttribute[];
	public bool IsNone      => Value is null or None;
	public bool IsError     => Value is SharpError;

	public SharpAttribute[] AsAttribute => (SharpAttribute[])Value!;
	public SharpError       AsError      => (SharpError)Value!;

	public CallState AsCallStateError => Value switch
	{
		None   => new CallState(Errors.ErrorNoSuchAttribute),
		SharpError e => new CallState(e.Value),
		_ => new CallState(Errors.ErrorNoSuchAttribute)
	};

	public CallState AsCallState => Value switch
	{
		SharpAttribute[] attrs => new CallState(attrs.Last().Value),
		None => new CallState(Errors.ErrorNoSuchAttribute),
		SharpError e => new CallState(e.Value),
		_ => new CallState(Errors.ErrorNoSuchAttribute)
	};
}
