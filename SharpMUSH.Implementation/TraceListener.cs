using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace SharpMUSH.Implementation;

public class TraceListener : IParseTreeListener
{
	public void EnterEveryRule(ParserRuleContext ctx)
	{
		Console.WriteLine($"Entering rule: {ctx.GetType().Name}");
	}

	public void ExitEveryRule(ParserRuleContext ctx)
	{
		Console.WriteLine($"Exiting rule: {ctx.GetType().Name}");
	}

	public void VisitErrorNode(IErrorNode node)
	{
		Console.WriteLine($"Error node: {node.Symbol.Text}");
	}

	public void VisitTerminal(ITerminalNode node)
	{
		Console.WriteLine($"Terminal node: {node.Symbol.Text}");
	}
}
