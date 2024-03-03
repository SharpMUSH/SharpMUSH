namespace AntlrCSharp.Implementation.Definitions
{
	public readonly struct DBRef
	{
		public DBRef(int number) => Number = number;

		public int Number { get; init; }
	}
}