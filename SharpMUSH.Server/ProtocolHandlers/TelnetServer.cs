using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database;
using System.Text;

namespace SharpMUSH.Server.ProtocolHandlers
{
	public class TelnetServer : ConnectionHandler
	{
		private readonly ILogger _Logger;

		public TelnetServer(ILogger<TelnetServer> logger, ISharpDatabase database) : base()
		{
			Console.OutputEncoding = Encoding.UTF8;
			_Logger = logger;

			_Logger.LogInformation("Starting Database");
			database.Migrate();
		}

		public override Task OnConnectedAsync(ConnectionContext connection)
		{
			throw new NotImplementedException();
		}
	}
}