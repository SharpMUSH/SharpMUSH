namespace SharpMUSH.Database
{
	public interface ISharpDatabase
	{
		Task Migrate();
	}
}