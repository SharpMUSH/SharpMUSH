using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands;

/// <summary>
/// Command to add a message to the channel recall buffer
/// </summary>
/// <param name="Message">The channel message to add to the buffer</param>
public record AddChannelMessageCommand(SharpChannelMessage Message) : ICommand;
