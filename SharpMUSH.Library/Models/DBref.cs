namespace SharpMUSH.Library.Models
{
	public readonly struct DBRef
	{
		public DBRef(int number) => Number = number;

		public DBRef(int number, int? milliseconds)
		{
			Number = number;
			CreationMilliseconds = milliseconds;
		}

		public int Number { get; init; }
		public int? CreationMilliseconds { get; init; }
	}
}