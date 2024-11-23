using Mediator;

namespace SharpMUSH.Library.Requests;

public record UpdateMSSPRequest(string Handle, TelnetNegotiationCore.Models.MSSPConfig Config) : INotification;