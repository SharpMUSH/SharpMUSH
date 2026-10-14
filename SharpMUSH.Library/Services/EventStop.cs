namespace SharpMUSH.Library.Services;

/// <summary>
/// Whether the event handler attribute of a stoppable event now running asked, with <c>stopevent()</c>, that the
/// attributes after it not run. Present only while a stoppable event's attribute runs.
/// </summary>
public sealed class EventStop
{
	private static readonly AsyncLocal<EventStop?> Ambient = new();

	/// <summary>The stoppable event being heard here, or null outside one.</summary>
	public static EventStop? Current => Ambient.Value;

	/// <summary>True once <see cref="Request"/> has been called.</summary>
	public bool Requested { get; private set; }

	/// <summary>Keeps the attributes after this one from running.</summary>
	public void Request() => Requested = true;

	/// <summary>Makes <paramref name="stop"/> <see cref="Current"/> until the result is disposed.</summary>
	public static IDisposable Enter(EventStop stop)
	{
		var previous = Ambient.Value;
		Ambient.Value = stop;
		return new Scope(previous);
	}

	private sealed class Scope(EventStop? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
