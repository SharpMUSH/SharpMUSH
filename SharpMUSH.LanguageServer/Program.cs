using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using Serilog;
using SharpMUSH.Implementation;
using SharpMUSH.LanguageServer.Handlers;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

// Configure Serilog for logging
var logPath = Path.Combine(
	Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
	"SharpMUSH",
	"lsp-server.log");

var logDir = Path.GetDirectoryName(logPath);
if (!string.IsNullOrEmpty(logDir))
{
	Directory.CreateDirectory(logDir);
}

Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Debug()
	.WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
	.CreateLogger();

try
{
	Log.Information("Starting SharpMUSH Language Server...");

	var server = await LanguageServer.From(options =>
		options
			.WithInput(Console.OpenStandardInput())
			.WithOutput(Console.OpenStandardOutput())
			.ConfigureLogging(builder => builder
				.AddSerilog(Log.Logger)
				.SetMinimumLevel(LogLevel.Debug))
			.WithServices(services =>
			{
				// Register document manager
				services.AddSingleton<DocumentManager>();

				// Register the underlying MUSH code parser with minimal dependencies
				services.AddSingleton<IMUSHCodeParser>(sp =>
				{
					var logger = sp.GetRequiredService<ILogger<MUSHCodeParser>>();
					
					// Create minimal libraries - LSP doesn't need full runtime
					var functionLibrary = new LibraryService<string, FunctionDefinition>();
					var commandLibrary = new LibraryService<string, CommandDefinition>();
					
					// Create a minimal options wrapper
					var options = new MinimalOptionsWrapper();
					
					// Create the parser with minimal dependencies
					return new MUSHCodeParser(logger, functionLibrary, commandLibrary, options, sp);
				});

				// Register the stateless LSP-specific parser wrapper
				services.AddSingleton<LSPMUSHCodeParser>(sp =>
				{
					var parser = sp.GetRequiredService<IMUSHCodeParser>();
					return new LSPMUSHCodeParser(parser);
				});
			})
			.WithHandler<TextDocumentSyncHandler>()
			.WithHandler<SemanticTokensHandler>()
			.WithHandler<CompletionHandler>()
			.WithHandler<HoverHandler>()
			.WithHandler<DefinitionHandler>()
			.WithHandler<ReferencesHandler>()
			.WithHandler<CodeActionHandler>()
			.WithHandler<SignatureHelpHandler>()
			.WithHandler<DocumentSymbolHandler>()
			.WithHandler<RenameHandler>()
			.WithHandler<DocumentFormattingHandler>()
			.WithHandler<WorkspaceSymbolsHandler>()
			.OnInitialize(async (server, request, cancellationToken) =>
			{
				Log.Information("Language server initialized");
				return;
			})
			.OnInitialized(async (server, request, response, cancellationToken) =>
			{
				Log.Information("Language server ready");
				return;
			}));

	Log.Information("Language server started successfully");
	await server.WaitForExit;
}
catch (Exception ex)
{
	Log.Fatal(ex, "Language server terminated unexpectedly");
}
finally
{
	Log.CloseAndFlush();
}

/// <summary>
/// Minimal implementation of IOptionsWrapper for LSP usage.
/// The LSP server doesn't need full runtime configuration.
/// </summary>
file class MinimalOptionsWrapper : IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>
{
	public SharpMUSH.Configuration.Options.SharpMUSHOptions CurrentValue => 
		throw new NotSupportedException("LSP parser does not support runtime options");
}
