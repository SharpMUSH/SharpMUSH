using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Moves <paramref name="Character"/>'s read marker for <paramref name="Marker"/>'s scope to it, unless the
/// stored one is already as far on; answers with the marker as stored afterwards. See
/// <see cref="IReadMarkerStore.AdvanceReadMarkerAsync"/>.
/// </summary>
public record AdvanceReadMarkerCommand(DBRef Character, SharpReadMarker Marker) : ICommand<SharpReadMarker>;
