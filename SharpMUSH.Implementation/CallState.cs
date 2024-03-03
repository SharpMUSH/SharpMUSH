namespace AntlrCSharp.Implementation
{
	public record CallState(MString? Message, int Depth = 0)
	{
		public CallState(string Message, int Depth = 0) : this(MModule.single(Message ?? ""), Depth) { }
	}
}
