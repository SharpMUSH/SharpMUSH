using System.Collections.Concurrent;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// PennMUSH's <c>QUEUE_PRESERVE_QREG</c> and <c>QUEUE_CLEAR_QREG</c> around one run of code: the
/// <c>/localize</c> and <c>/clearregs</c> switches of <c>@include</c>, <c>@force</c>, an inline
/// <c>@switch</c>/<c>@select</c> action and an inline <c>@hook</c>.
/// <para>Entering saves the top q-register frame when localizing, then clears it when clearing (save
/// before clear, so the copy is the caller's registers). Disposing puts the saved registers back into
/// whatever frame is on top by then. <c>/clearregs</c> without <c>/localize</c> keeps no copy and so
/// restores nothing, which is <c>do_entry</c>'s bare <c>QUEUE_CLEAR_QREG</c> case.</para>
/// </summary>
/// <example><code>using var registers = RegisterScope.Enter(parser.CurrentState.Registers, localize, clear);</code></example>
public readonly struct RegisterScope : IDisposable
{
	private readonly ConcurrentStack<Dictionary<string, MString>>? _stack;
	private readonly Dictionary<string, MString>? _saved;

	private RegisterScope(ConcurrentStack<Dictionary<string, MString>> stack, Dictionary<string, MString> saved)
	{
		_stack = stack;
		_saved = saved;
	}

	/// <summary>Saves and/or clears the top q-register frame of <paramref name="registers"/>.</summary>
	public static RegisterScope Enter(ConcurrentStack<Dictionary<string, MString>> registers, bool localize, bool clear)
	{
		if ((!localize && !clear) || !registers.TryPeek(out var top))
		{
			return default;
		}

		var saved = localize ? new Dictionary<string, MString>(top) : null;

		if (clear)
		{
			top.Clear();
		}

		return saved is null ? default : new RegisterScope(registers, saved);
	}

	/// <summary>Restores the saved registers, when there are any.</summary>
	public void Dispose()
	{
		if (_saved is null || _stack is null || !_stack.TryPeek(out var top))
		{
			return;
		}

		top.Clear();
		foreach (var (key, value) in _saved)
		{
			top[key] = value;
		}
	}
}
