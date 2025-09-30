using Mediator;
using TelnetNegotiationCore.Models;

namespace SharpMUSH.Library.Notifications;

public record UpdateMSSPNotification(long handle, MSSPConfig Config) : INotification;