using Mediator;

namespace SharpMUSH.Library.Notifications;

public record UpdateMSDPNotification(long handle, string ResetVariable) : INotification;