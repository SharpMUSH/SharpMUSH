using Mediator;

namespace SharpMUSH.Library.Requests;

public record UpdateMSDPRequest(string Handle, string ResetVariable) : INotification;