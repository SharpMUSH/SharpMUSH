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
