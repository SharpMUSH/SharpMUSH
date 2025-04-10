using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Requests;

public record QueueCommandListRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue) : IRequest;

public record QueueDelayedCommandListRequest(
	MString Command,
	ParserState State,
	TimeSpan Delay) : IRequest;
	
public record QueueCommandListWithTimeoutRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue, 
	TimeSpan Timeout) : IRequest;