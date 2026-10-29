using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.ParserInterfaces;

public enum LockRenderMode { Readback, Examine, Decompile }

public interface IBooleanExpressionParser
{
	Func<AnySharpObject, AnySharpObject, ValueTask<bool>> Compile(string text);
	bool Validate(string text, AnySharpObject lockee);
	bool IsBound(string text);
	void InvalidateCache(string? text = null);
	/// <summary>Formats syntax without resolving names. Use BindAsync before storing a lock.</summary>
	string Normalize(string text);
	ValueTask<Result<string>> BindAsync(string text, AnySharpObject executor, CancellationToken cancellationToken = default);
	ValueTask<string> RenderAsync(string text, AnySharpObject viewer, LockRenderMode mode, CancellationToken cancellationToken = default);
}

public static class BooleanExpressionParserExtensions
{
	/// <summary>
	/// A stored lock key as it is shown and handed to softcode: each live object it names as the <c>#N</c> it was
	/// set with rather than the objid the store keeps. An empty key is unlocked and stays empty.
	/// </summary>
	public static async ValueTask<string> ReadbackAsync(this IBooleanExpressionParser parser, string key, AnySharpObject viewer,
		CancellationToken cancellationToken = default)
		=> string.IsNullOrEmpty(key) ? key : await parser.RenderAsync(key, viewer, LockRenderMode.Readback, cancellationToken);
}
