using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of LazySharpAttribute[], None, and SharpError.
/// Replaces OptionalLazySharpAttributeOrError : OneOfBase&lt;LazySharpAttribute[], None, Error&lt;string&gt;&gt;.
/// </summary>
public union OptionalLazySharpAttributeOrError(LazySharpAttribute[], None, SharpError)
{
	public bool IsAttribute => Value is LazySharpAttribute[];
	public bool IsNone      => Value is null or None;
	public bool IsError     => Value is SharpError;

	public LazySharpAttribute[] AsAttribute => (LazySharpAttribute[])Value!;
	public SharpError           AsError      => (SharpError)Value!;

	public CallState AsCallStateError => Value switch
	{
		None => new CallState(Errors.ErrorNoSuchAttribute),
		SharpError e => new CallState(e.Value),
		_ => new CallState(Errors.ErrorNoSuchAttribute)
	};

	public async ValueTask<CallState> AsCallStateAsync() => Value switch
	{
		LazySharpAttribute[] attrs => await attrs.Last().Value.WithCancellation(CancellationToken.None),
		None => new CallState(Errors.ErrorNoSuchAttribute),
		SharpError e => new CallState(e.Value),
		_ => new CallState(Errors.ErrorNoSuchAttribute)
	};
}
