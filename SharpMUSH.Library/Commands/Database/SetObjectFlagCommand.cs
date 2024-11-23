using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectFlagCommand(AnySharpObject Target, SharpObjectFlag Flag) : ICommand<bool>, ICacheInvalidating;
