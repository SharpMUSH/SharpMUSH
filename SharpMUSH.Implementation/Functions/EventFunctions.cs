using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Functions;

/// <summary>The event functions (<c>help events</c>).</summary>
public partial class Functions
{
	private const string NotInStoppableEvent = "#-1 NOT HEARING A STOPPABLE EVENT";

	/// <summary>
	/// Keeps the event handler's attributes after this one from running for the stoppable event being run. Called
	/// anywhere else, it returns an error and stops nothing.
	/// </summary>
	[SharpFunction(Name = "STOPEVENT", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> StopEvent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (EventStop.Current is not { } stop)
		{
			return ValueTask.FromResult(new CallState(NotInStoppableEvent));
		}

		stop.Request();
		return ValueTask.FromResult(CallState.Empty);
	}
}
