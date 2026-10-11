using System.Net.WebSockets;
using System.Text;
using SharpMUSH.AnalysisProtocol;
using SharpMUSH.LspBridge;

if (args.Length != 2 || args[0] != "--endpoint")
{
	Console.Error.WriteLine("Usage: dotnet SharpMUSH.LspBridge.dll --endpoint wss://game.example/lsp");
	return 2;
}
try
{
	var endpoint = RemoteStdioBridge.ValidateEndpoint(args[1]);
	var character = Environment.GetEnvironmentVariable("SHARPMUSH_LSP_CHARACTER");
	var password = Environment.GetEnvironmentVariable("SHARPMUSH_LSP_PASSWORD");
	Environment.SetEnvironmentVariable("SHARPMUSH_LSP_CHARACTER", null);
	Environment.SetEnvironmentVariable("SHARPMUSH_LSP_PASSWORD", null);
	if (string.IsNullOrWhiteSpace(character) || character.Contains(':') || string.IsNullOrEmpty(password))
	{
		Console.Error.WriteLine("Provide character credentials in the private child environment.");
		return 2;
	}
	using var socket = new ClientWebSocket();
	socket.Options.AddSubProtocol(LspFrames.Subprotocol);
	socket.Options.SetRequestHeader("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(character + ":" + password)));
	// No cross-host redirects carrying credentials, and no custom certificate bypass.
	using var invoker = new HttpMessageInvoker(new SocketsHttpHandler { AllowAutoRedirect = false });
	using var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(30));
	await socket.ConnectAsync(endpoint, invoker, connecting.Token);
	await RemoteStdioBridge.RunAsync(socket, Console.OpenStandardInput(), Console.OpenStandardOutput());
	return 0;
}
catch (Exception)
{
	// URL/userinfo and headers never appear in error logs or protocol stdout.
	Console.Error.WriteLine("Remote LSP connection or framing failed.");
	return 1;
}
