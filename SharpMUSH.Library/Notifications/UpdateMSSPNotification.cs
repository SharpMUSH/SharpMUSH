using Mediator;

namespace SharpMUSH.Library.Notifications;

public record UpdateMSSPNotification(long handle, TelnetNegotiationCore.Models.MSSPConfig Config) : INotification;