namespace SharpMUSH.Library.Models
{
	public readonly struct DBRef
	{
		public DBRef(int number) => Number = number;

		public DBRef(int number, long? milliseconds)
		{
			Number = number;
			CreationMilliseconds = milliseconds;
		}

		public int Number { get; init; }
		public long? CreationMilliseconds { get; init; }

		public override string ToString() 
			=> $"#{Number}:{CreationMilliseconds}";
	}
}