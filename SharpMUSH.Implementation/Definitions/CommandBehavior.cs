namespace SharpMUSH.Implementation.Definitions
{
	[Flags]
	public enum CommandBehavior
	{
		Undefined = 0,
		NoParse = 1,
		EqSplit = 2,
		LSArgs = 4,
		RSArgs = 8,
		RSNoParse = 16
	}
}
