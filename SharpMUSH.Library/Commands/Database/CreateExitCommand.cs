using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateExitCommand(string Name, string[] Aliases, AnySharpContainer Location, SharpPlayer Creator) : ICommand<DBRef>, ICacheInvalidating;