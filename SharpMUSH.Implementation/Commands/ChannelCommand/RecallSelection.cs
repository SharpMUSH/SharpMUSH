using System.Diagnostics.CodeAnalysis;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// The recall window to render, or the <see cref="CallState"/> to return because there is none — the
/// error has already been reported.
/// </summary>
public partial union RecallSelection(ChannelRecall.RecallWindow, CallState)
{
	/// <summary>True with the window to render, or false with the <see cref="CallState"/> to return.</summary>
	public bool TryGetWindow(out ChannelRecall.RecallWindow window, [MaybeNullWhen(true)] out CallState refusal)
	{
		switch (Value)
		{
			case ChannelRecall.RecallWindow selected:
				window = selected;
				refusal = default;
				return true;
			case CallState held:
				window = default;
				refusal = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(RecallSelection)} holds neither case.");
		}
	}
}
