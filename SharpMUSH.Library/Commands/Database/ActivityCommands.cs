using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Adds a batch of counted activity to what is kept. See <see cref="IActivityStore.MergeActivityAsync"/>.
/// <see cref="Queries.Database.GetActivityQuery"/> is not cached, so nothing is invalidated.
/// </summary>
public record MergeActivityCommand(ActivityBatch Batch) : ICommand;
