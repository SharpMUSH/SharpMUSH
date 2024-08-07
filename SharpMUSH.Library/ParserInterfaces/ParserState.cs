using SharpMUSH.Library.Models;
using System.Collections.Immutable;

namespace SharpMUSH.Library.ParserInterfaces;

public record ParserState(
	Stack<ImmutableDictionary<string, MString>> Registers,
	DBAttribute? CurrentEvaluation,
	string? Function,
	string? Command,
	List<CallState> Arguments,
	DBRef? Executor,
	DBRef? Enactor,
	DBRef? Caller,
	string? Handle);