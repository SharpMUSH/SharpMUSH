using Mediator;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Importer only: lowers the dbref counter to one past the highest object. See
/// <see cref="IObjectStore.ReleaseTrailingDbrefsAsync"/>.
/// </summary>
public record ReleaseTrailingDbrefsCommand : ICommand<int>;
