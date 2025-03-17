using Mediator;

namespace SharpMUSH.Library.Notifications;

public record TelnetInputNotification(string Handle, string Input) : INotification;