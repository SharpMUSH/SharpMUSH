using Mediator;

namespace SharpMUSH.Library.Notifications;

public record UpdateMSSPNotification(string Handle, TelnetNegotiationCore.Models.MSSPConfig Config) : INotification;