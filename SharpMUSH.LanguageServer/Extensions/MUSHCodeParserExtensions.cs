using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.LanguageServer.Extensions;

/// <summary>
/// Extension methods for creating a minimal MUSH parser for LSP usage.
/// </summary>
public static class MUSHCodeParserExtensions
{
	/// <summary>
	/// Creates a minimal MUSH parser instance suitable for LSP operations.
	/// This parser only supports syntax validation and semantic token generation,
	/// not full runtime execution.
	/// </summary>
	public static IMUSHCodeParser CreateForLSP(ILogger<MUSHCodeParser> logger, IServiceProvider serviceProvider)
	{
		// Create minimal libraries - LSP doesn't need full runtime
		var functionLibrary = new LibraryService<string, FunctionDefinition>();
		var commandLibrary = new LibraryService<string, CommandDefinition>();
		
		// Create a minimal options wrapper with null object pattern
		var options = new MinimalOptionsWrapper();
		
		// Create the parser with minimal dependencies
		var parser = new MUSHCodeParser(logger, functionLibrary, commandLibrary, options, serviceProvider);
		
		return parser;
	}
	
	/// <summary>
	/// Minimal implementation of IOptionsWrapper for LSP usage.
	/// </summary>
	private class MinimalOptionsWrapper : IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>
	{
		public SharpMUSH.Configuration.Options.SharpMUSHOptions CurrentValue => 
			throw new NotSupportedException("LSP parser does not support runtime options");
	}
}
