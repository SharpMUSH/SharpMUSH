using Mediator;

namespace SharpMUSH.Library.Notifications;

public record SignalGMCPNotification(string Handle, string Module, string Writeback) : INotification;