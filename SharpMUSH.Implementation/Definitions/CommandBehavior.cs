namespace SharpMUSH.Implementation.Definitions;

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
		SingleToken = 64,
		NoGagged = 128,
		NoGuest = 256,
		Player = 512,
		Switches = 1024,
		RSBrace = 2048,
		Thing = 4096,
		Internal = 8192,
		NoOp = 16384,
		God = 32768,
		Args = 65536
}