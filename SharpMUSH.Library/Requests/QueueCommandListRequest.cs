using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Requests;

// Future enhancement: Return the new PID for output/tracking
// Currently IRequest doesn't support return values
public record QueueCommandListRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue) : IRequest;

public record QueueAttributeRequest(
	Func<ValueTask<ParserState>> Input,
	DbRefAttribute DbRefAttribute) : IRequest;

public record QueueDelayedCommandListRequest(
	MString Command,
	ParserState State,
	TimeSpan Delay) : IRequest;

// Future enhancement: Return the new PID for output/tracking
// Currently IRequest doesn't support return values
public record QueueCommandListWithTimeoutRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue, 
	TimeSpan Timeout) : IRequest;