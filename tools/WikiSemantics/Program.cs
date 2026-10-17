using SharpMUSH.Tools.WikiSemantics;

if (args.Length != 5 || args[0] != "dry-run" || args[1] != "--source" || args[3] != "--output")
{
	Console.Error.WriteLine("Usage: dry-run --source <pinned-pf2e-remaster-directory> --output <new-directory>");
	return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
	return DryRunReport.Run(args[2], args[4], cancellation.Token) switch
	{
		DryRunSuccess success => Complete(success),
		DatasetFailure failure => Fail(failure)
	};
}
catch (OperationCanceledException)
{
	Console.Error.WriteLine("Dry run cancelled.");
	return 130;
}
static int Complete(DryRunSuccess success)
{
	Console.WriteLine($"Verified {success.Pages} pages; wrote {success.Facts} canonical facts.");
	return 0;
}
static int Fail(DatasetFailure failure)
{
	Console.Error.WriteLine(failure.Detail);
	return 1;
}
