using Mediator;

namespace SharpMUSH.Library.Notifications;

public record TelnetOutputNotification(string[] Handles, string Output) : INotification;