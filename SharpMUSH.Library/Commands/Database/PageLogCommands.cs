using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Keeps <paramref name="Page"/> in the page log for each of <paramref name="Owners"/>. See
/// <see cref="IPageLogStore.RecordPageAsync"/>. Nothing reads the log through the cache, so nothing is
/// invalidated.
/// </summary>
public record RecordPageCommand(SharpPage Page, IReadOnlyList<DBRef> Owners) : ICommand;

/// <summary>
/// Deletes every logged page sent before <paramref name="Before"/>; answers with how many copies went. See
/// <see cref="IPageLogStore.PurgePageLogAsync"/>.
/// </summary>
public record PurgePageLogCommand(DateTimeOffset Before) : ICommand<int>;
