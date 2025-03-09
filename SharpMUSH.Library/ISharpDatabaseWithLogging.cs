namespace SharpMUSH.Library;

public interface ISharpDatabaseWithLogging
{
	public ValueTask SetupLogging();
}