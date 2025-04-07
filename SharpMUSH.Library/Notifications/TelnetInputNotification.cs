using Mediator;

namespace SharpMUSH.Library.Notifications;

public record TelnetInputNotification(long Handle, string Input) : INotification;