using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace SharpMUSH.Server.Authentication.External;
/// <summary>
/// Framework remote handlers log token/parser exceptions, protocol errors and challenge cookies even at
/// debug/trace. Keep event ids and severity for diagnosis, but never submit protocol payloads to a logger.
/// This wrapper is local to these named handlers and does not alter application logging.
/// </summary>
internal sealed class ExternalProtocolLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
	public ILogger CreateLogger(string categoryName) => new SafeLogger(inner.CreateLogger(categoryName));
	public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
	public void Dispose() { }
	private sealed class SafeLogger(ILogger logger) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		 => logger.Log(logLevel, eventId, "External authentication protocol event.", null, static (message, _) => message);
	}
}
/// <summary>Uses the unmodified framework OIDC validation and protocol flow with payload-free logging.</summary>
public sealed class ExternalOpenIdConnectHandler(IOptionsMonitor<OpenIdConnectOptions> options, ILoggerFactory logger,
 HtmlEncoder htmlEncoder, UrlEncoder encoder) : OpenIdConnectHandler(options, new ExternalProtocolLoggerFactory(logger), htmlEncoder, encoder);
