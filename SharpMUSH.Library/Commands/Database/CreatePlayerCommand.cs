using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreatePlayerCommand(string Name, string Password, DBRef Location) : ICommand<DBRef>, ICacheInvalidating;