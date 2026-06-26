using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles text document synchronization events (open, change, close).
/// </summary>
public class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly LSPMUSHCodeParser _parser;
	private readonly ILanguageServerFacade _languageServer;

	public TextDocumentSyncHandler(
		DocumentManager documentManager,
		LSPMUSHCodeParser parser,
		ILanguageServerFacade languageServer)
	{
		_documentManager = documentManager;
		_parser = parser;
		_languageServer = languageServer;
	}

	public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
	{
		return new TextDocumentAttributes(uri, "mush");
	}

	public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var text = request.TextDocument.Text;
		var version = request.TextDocument.Version ?? 0;

		_documentManager.OpenDocument(uri, text, version);

		PublishDiagnostics(uri, text);

		return Unit.Task;
	}

	public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var version = request.TextDocument.Version ?? 0;

		var text = request.ContentChanges.LastOrDefault()?.Text ?? string.Empty;

		_documentManager.UpdateDocument(uri, text, version);

		PublishDiagnostics(uri, text);

		return Unit.Task;
	}

	public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		_documentManager.CloseDocument(uri);

		_languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
		{
			Uri = request.TextDocument.Uri,
			Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>()
		});

		return Unit.Task;
	}

	public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
	{
		return Unit.Task;
	}

	protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
		TextSynchronizationCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new TextDocumentSyncRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu"),
			Change = TextDocumentSyncKind.Full,
			Save = new SaveOptions { IncludeText = false }
		};
	}

	private void PublishDiagnostics(string uri, string text)
	{
		try
		{
			var diagnostics = _parser.GetDiagnostics(text, ParseType.Function);

			var lspDiagnostics = diagnostics.Select(d => new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
			{
				Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
					new Position(d.Range.Start.Line, d.Range.Start.Character),
					new Position(d.Range.End.Line, d.Range.End.Character)
				),
				Severity = (DiagnosticSeverity)(int)d.Severity,
				Code = d.Code != null ? new DiagnosticCode(d.Code) : (DiagnosticCode?)null,
				Source = d.Source ?? "SharpMUSH",
				Message = d.Message,
				Tags = d.Tags != null && d.Tags.Length > 0
					? new Container<DiagnosticTag>(d.Tags.Select(t => (DiagnosticTag)(int)t).ToArray())
					: null
			}).ToList();

			_languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
			{
				Uri = DocumentUri.From(uri),
				Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(lspDiagnostics)
			});
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error publishing diagnostics: {ex.Message}");
#pragma warning restore VSTHRD103
		}
	}
}
