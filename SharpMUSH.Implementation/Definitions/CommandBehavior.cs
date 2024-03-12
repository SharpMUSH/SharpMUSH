namespace SharpMUSH.Implementation.Definitions
{
	[Flags]
	public enum CommandBehavior
	{
		Default = 0,
		NoParse = 1,
		EqSplit = 2,
		LSArgs = 4,
		RSArgs = 8,
		RSNoParse = 16,
		SOCKET = 32,
		SingleToken = 64
	}
}
