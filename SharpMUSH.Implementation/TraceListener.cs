using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace SharpMUSH.Implementation;

public class TraceListener : IParseTreeListener
{
	public void EnterEveryRule(ParserRuleContext ctx)
	{
		// Print debug information when entering a rule
		Console.WriteLine($"Entering rule: {ctx.GetType().Name}");
	}

	public void ExitEveryRule(ParserRuleContext ctx)
	{
		// Print debug information when exiting a rule
		Console.WriteLine($"Exiting rule: {ctx.GetType().Name}");
	}

	public void VisitErrorNode(IErrorNode node)
	{
		// Print debug information when visiting an error node
		Console.WriteLine($"Error node: {node.Symbol.Text}");
	}

	public void VisitTerminal(ITerminalNode node)
	{
		// Print debug information when visiting a terminal node
		Console.WriteLine($"Terminal node: {node.Symbol.Text}");
	}
}
