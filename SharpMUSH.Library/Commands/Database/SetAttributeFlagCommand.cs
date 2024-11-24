using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetAttributeFlagCommand(SharpAttribute Target, SharpAttributeFlag Flag) : ICommand<bool>, ICacheInvalidating;
