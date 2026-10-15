using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Keeps one roll; answers with its id. See <see cref="IRollStore.RecordRollAsync"/>. The handler stamps
/// <see cref="RollDraft.At"/> from the server's clock, so a caller cannot backdate a roll. A kept roll never
/// changes, and <see cref="Queries.Database.GetRollQuery"/> is not cached, so nothing is invalidated.
/// </summary>
public record RecordRollCommand(RollDraft Draft) : ICommand<RecordedRoll>;
