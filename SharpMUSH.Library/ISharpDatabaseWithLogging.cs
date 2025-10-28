namespace SharpMUSH.Library;

public interface ISharpDatabaseWithLogging
{
	ValueTask SetupLogging();
}