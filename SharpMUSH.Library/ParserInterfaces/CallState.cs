namespace SharpMUSH.Library.ParserInterfaces;

public record CallState(MString? Message, int Depth, MString[]? Arguments)
{
	public CallState(MString? Message, int Depth) : this(Message ?? MModule.empty(), Depth, null) { }

	public CallState(MString? Message) : this(Message ?? MModule.empty(), 0, null) { }

	public CallState(string Message) : this(Message is not null ? MModule.single(Message) : MModule.empty(), 0, null) { }

	public CallState(bool Result, string errorIfFalse = "0") : this(MModule.single(Result? "1" : errorIfFalse), 0, null) { }

	public CallState(string Message, int Depth) : this(Message is not null ? MModule.single(Message) : MModule.empty(), Depth, null) { }

	public static CallState EmptyArgument = new CallState(MModule.empty(), 0) with { Arguments = [] };
	public static CallState Empty = new CallState(MModule.empty(), 0);
}
