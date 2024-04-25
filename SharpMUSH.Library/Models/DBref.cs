
namespace SharpMUSH.Library.Models
{
	public readonly struct DBRef : IEquatable<DBRef>
	{
		public DBRef(int number) => Number = number;

		public DBRef(int number, long? milliseconds)
		{
			Number = number;
			CreationMilliseconds = milliseconds;
		}

		public int Number { get; init; }
		public long? CreationMilliseconds { get; init; }

		public override bool Equals(object? obj)
		{
			return obj is DBRef @ref && Equals(@ref);
		}

		public bool Equals(DBRef other)
		{
			return Number == other.Number &&
						 CreationMilliseconds == other.CreationMilliseconds;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Number, CreationMilliseconds);
		}

		public override string ToString() 
			=> $"#{Number}:{CreationMilliseconds}";

		public static bool operator ==(DBRef left, DBRef right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DBRef left, DBRef right)
		{
			return !(left == right);
		}
	}
}