namespace SharpMUSH.Database;

/// <summary>
/// Wraps an <see cref="IAsyncEnumerable{T}"/> factory so that every call to
/// <see cref="IAsyncEnumerable{T}.GetAsyncEnumerator"/> builds a brand-new state machine.
/// </summary>
/// <remarks>
/// A C# <c>async IAsyncEnumerable</c> method returns its own state machine, and that object's
/// <c>GetAsyncEnumerator</c> hands <em>itself</em> back to the first caller when the machine is in its
/// initial state and the calling thread is the one that created it. Only later callers get a copy.
/// The models cache one such object per property behind a <see cref="Lazy{T}"/> and every call site
/// enumerates that same instance, so two consumers on the same pooled thread id can end up sharing one
/// state machine — enumeration #2 takes <c>this</c> in the window after enumeration #1 finished and
/// before it disposed. What follows is one of:
/// <list type="bullet">
///   <item>#1's <c>DisposeAsync</c> lands while #2 is running, and the compiler-generated
///     <c>DisposeAsync</c> throws <see cref="NotSupportedException"/> ("Specified method is not
///     supported.") — the crash in issue #798, from <c>HasFlag</c>'s <c>AnyAsync</c>.</item>
///   <item>#1's <c>DisposeAsync</c> lands while #2 is suspended at a <c>yield return</c>, which runs the
///     machine to completion and silently ends #2's enumeration early — a short list, with no error.</item>
/// </list>
/// Calling the iterator method afresh per enumeration gives each consumer a state machine no one else
/// holds a reference to, which removes both. It costs one allocation per enumeration; the alternative
/// (each enumeration after the first) already allocates a copy.
/// </remarks>
public sealed class FreshAsyncEnumerable<T>(Func<IAsyncEnumerable<T>> factory) : IAsyncEnumerable<T>
{
	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		=> factory().GetAsyncEnumerator(cancellationToken);
}
