using Mediator;

namespace SharpMUSH.Library.Notifications;

public record SignalGMCPNotification(long handle, string Module, string Writeback) : INotification;