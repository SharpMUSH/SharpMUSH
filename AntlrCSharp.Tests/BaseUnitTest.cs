using Serilog;

namespace AntlrCSharp.Tests
{
	public class BaseUnitTest
	{
		public BaseUnitTest()
		{
			Log.Logger = new LoggerConfiguration()
																			.WriteTo.Console()
																			.MinimumLevel.Debug()
																			.CreateLogger();
		}
	}
}
