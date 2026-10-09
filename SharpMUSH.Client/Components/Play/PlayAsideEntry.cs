using System.Text.Json;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Components.Play;

/// <summary>One entry of Play's aside and of the phone's Room sheet: a layout placement or a scope panel, by widget name.</summary>
/// <param name="Placed">From the play layout; otherwise a scope panel the page appends.</param>
public sealed record PlayAsideEntry(string Name, IPortalWidget Widget, JsonElement? Config, bool Placed);
