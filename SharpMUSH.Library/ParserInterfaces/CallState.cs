namespace SharpMUSH.Library.ParserInterfaces;

public record CallState(MString? Message, int Depth = 0, string[]? Arguments = null)
{
	public CallState(string Message, int Depth = 0) : this(MModule.single(Message ?? string.Empty), Depth) { }
}
