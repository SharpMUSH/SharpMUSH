using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of IAsyncEnumerable&lt;LazySharpAttribute&gt; and SharpError.
/// Replaces LazySharpAttributesOrError : OneOfBase&lt;IAsyncEnumerable&lt;LazySharpAttribute&gt;, Error&lt;string&gt;&gt;.
/// </summary>
public union LazySharpAttributesOrError(IAsyncEnumerable<LazySharpAttribute>, SharpError)
{
	public static LazySharpAttributesOrError FromAsync(IAsyncEnumerable<LazySharpAttribute> x) => x;

	public bool IsAttribute => Value is IAsyncEnumerable<LazySharpAttribute>;
	public bool IsError     => Value is SharpError;

	public IAsyncEnumerable<LazySharpAttribute> AsAttributes => (IAsyncEnumerable<LazySharpAttribute>)Value!;
	public SharpError                           AsError       => (SharpError)Value!;
}
