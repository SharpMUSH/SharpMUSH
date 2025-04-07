using Mediator;

namespace SharpMUSH.Library.Notifications;

public record UpdateNAWSNotification(long Handle, int Height, int Width) : INotification;