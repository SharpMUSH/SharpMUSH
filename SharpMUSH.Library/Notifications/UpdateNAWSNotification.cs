using Mediator;

namespace SharpMUSH.Library.Notifications;

public record UpdateNAWSNotification(string Handle, int Height, int Width) : INotification;