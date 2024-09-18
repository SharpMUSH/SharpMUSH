using MediatR;

namespace SharpMUSH.Library.Requests;

public record SignalGMCPRequest(string Handle, string Module, string Writeback) : INotification;