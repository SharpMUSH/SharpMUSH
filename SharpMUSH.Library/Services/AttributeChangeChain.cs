namespace SharpMUSH.Library.Services;

/// <summary>
/// How many attribute watch handlers (<c>@attribute/watch</c>) stand behind the code now running: 0 for anything a
/// player typed or the server started, 1 inside the handler of a change, 2 inside the handler of a change that
/// handler made, and so on. A handler that writes the attribute it heard about (or two watched attributes whose
/// handlers write each other) would otherwise run again for ever, one queue entry after another, which no queue
/// limit stops because each entry finishes before the next is queued.
/// </summary>
/// <remarks>
/// The depth travels with the code, not the object: the handler's queue entry carries it in
/// <see cref="ParserInterfaces.ParserState.AttributeChangeDepth"/>, every action that entry queues
/// (<c>@trigger</c>, <c>@wait</c>, <c>@force</c>) keeps it, and the parser makes it ambient here while that
/// code runs, so the attribute write handlers, which see no parser state, can read it.
/// </remarks>
public static class AttributeChangeChain
{
	private static readonly AsyncLocal<int> Ambient = new();

	/// <summary>
	/// The deepest a chain of changes may go. A change made at this depth is written and announced to nobody;
	/// no watch handler runs and a warning is logged.
	/// </summary>
	public const int MaxDepth = 10;

	/// <summary>The depth of the code now running.</summary>
	public static int Depth => Ambient.Value;

	/// <summary>Makes <paramref name="depth"/> the <see cref="Depth"/> until the result is disposed.</summary>
	public static IDisposable Enter(int depth)
	{
		var previous = Ambient.Value;
		Ambient.Value = depth;
		return new Scope(previous);
	}

	private sealed class Scope(int previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
