using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateThingCommand(string Name, AnySharpContainer Where, SharpPlayer Owner) : ICommand<DBRef>, ICacheInvalidating;