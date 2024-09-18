using MediatR;

namespace SharpMUSH.Library.Requests;

public record UpdateNAWSRequest(string Handle, int Height, int Width) : INotification;