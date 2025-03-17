using Mediator;

namespace SharpMUSH.Library.Notifications;

public record UpdateMSDPNotification(string Handle, string ResetVariable) : INotification;