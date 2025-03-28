namespace SharpMUSH.Library.Models;

public readonly struct DbRefAttribute(DBRef dbref, string[] attribute)
	: IEquatable<DbRefAttribute>
{
	public DBRef DbRef { get; } = dbref;
	public string[] Attribute { get; } = attribute;

	public override bool Equals(object? obj) => obj is DBRef @ref && Equals(@ref);

	public bool Equals(DbRefAttribute other)
		=> other.DbRef.Equals(DbRef) && other.Attribute.SequenceEqual(Attribute);

	public bool Equals(DBRef other)
		=> other.Equals(DbRef);

	public override int GetHashCode()
		=> HashCode.Combine(DbRef.GetHashCode(), string.Join("`", Attribute));

	public override string ToString()
		=> DbRef + string.Join("`", Attribute);

	public static bool operator ==(DbRefAttribute left, DbRefAttribute right) => left.Equals(right);

	public static bool operator !=(DbRefAttribute left, DbRefAttribute right) => !(left == right);

	public static bool TryParse(string parse, out DbRefAttribute? output)
	{
		switch (HelperFunctions.SplitDBRefAndAttr(parse))
		{
			case { IsT0: true } split:
			{
				output = split.AsT0;
				return true;
			}
			default:
			{
				output = null;
				return false;
			}
		}
	}
}