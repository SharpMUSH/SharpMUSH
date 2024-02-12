using Antlr4.Runtime;
using Serilog;

public partial class PennMUSHLexer : Lexer
{
	private void OpenSubstitution()
	{
		Log.Logger.Information("Entering OpenSubstitution");
	}

	private void CloseSubstitution()
	{
		Log.Logger.Information("Exiting CloseSubstitution");
	}
}
