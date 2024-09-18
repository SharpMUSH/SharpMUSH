using MediatR;

namespace SharpMUSH.Library.Requests;

public record TelnetOutputRequest(string[] Handles, string Output) : INotification;