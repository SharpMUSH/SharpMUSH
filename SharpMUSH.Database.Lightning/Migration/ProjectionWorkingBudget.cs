namespace SharpMUSH.Database.Lightning.Migration;

internal sealed class ProjectionBudget(long maximum, long initial)
{
	public long Peak { get; private set; } = initial;
	public void Add(long bytes)
	{
		var next = checked(Peak + bytes);
		if (next > maximum) throw new WikiCategoryProjectionCapacityException();
		Peak = next;
	}
}

internal sealed class WikiCategoryProjectionCapacityException() : Exception("Projection working-byte capacity exceeded.")
{
	public string Dimension => "working-bytes";
}
