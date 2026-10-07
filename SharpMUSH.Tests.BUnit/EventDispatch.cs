using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// Raising an event on a rendered element. Tests await bUnit's <c>ClickAsync</c>, <c>InputAsync</c> and the
/// rest, never the void <c>Click()</c>, <c>Input()</c> ...: those hand the event to the renderer's dispatcher
/// and return without waiting, and when a render is in flight (an HTTP answer landing, an awaited load
/// finishing) the dispatcher queues the event behind it. The test then reads the markup before its own event
/// has run — or the event, bound to an element that render replaced, is refused and the failure is swallowed
/// with the discarded task. <c>BannedSymbols.txt</c> keeps the void forms out.
/// </summary>
public static class EventDispatch
{
	/// <summary>
	/// Clicks <paramref name="element"/> and returns once its handler has run up to its first wait, not once
	/// the handler has finished. For a handler that waits on the test itself — a confirmation dialog the test
	/// answers next — where awaiting <c>ClickAsync</c> would wait forever. The click still goes through the
	/// dispatcher, after any render in flight, and a click the renderer refuses still fails the test.
	/// </summary>
	public static Task StartClickAsync<TComponent>(this IRenderedComponent<TComponent> cut, IElement element)
		where TComponent : IComponent
		=> cut.InvokeAsync(() =>
		{
			// On the dispatcher already, so the click runs here and now; what is left of the handler
			// after its first await continues without this caller.
			_ = element.ClickAsync();
		});
}
