using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>One application instance's presentation, typed draft and admitted action.</summary>
public sealed class SchemaApplicationState
{
	private Guid _active;
	public SchemaFormModel Form { get; private set; } = new();
	public PortalSchemaDocument? Document => Form.Document;
	public SchemaData? Data { get; private set; }
	public bool Busy => _active != Guid.Empty;
	public string? Redirect { get; private set; }
	public string? Toast { get; private set; }

	/// <summary>Starts an unrelated document instance; nothing from the old actor or record survives.</summary>
	public void Load(PortalSchemaDocument? document, SchemaData? data)
	{
		Retire();
		Form = new();
		if (document is not null && !Supported(document))
		{
			Form.GlobalError = "Unsupported application schema: expected schema_version 1 and kind form or view.";
			Data = null;
			return;
		}
		Form.Load(document, data);
		Data = data;
	}

	private static bool Supported(PortalSchemaDocument document) => document.SchemaVersion == 1 && document.Kind?.ToLowerInvariant() is "form" or "view";

	public void UpdateData(SchemaData? data)
	{
		Retire();
		Data = data;
		Form.ApplyData(data);
	}

	public void Retire() { _active = Guid.Empty; Redirect = null; Toast = null; }

	/// <summary>Admission is serialized; empty means an action is already pending.</summary>
	public Guid Begin()
	{
		if (Busy) return Guid.Empty;
		Form.ClearErrors();
		Redirect = null;
		Toast = null;
		return _active = Guid.NewGuid();
	}

	/// <summary>Reduces an accepted response: reset, merge, presentation/data, defaults, errors.</summary>
	public bool Apply(Guid token, SchemaAction action, SchemaActionResult result, string failed)
	{
		if (token == Guid.Empty || token != _active) return false;
		_active = Guid.Empty;
		if (result.Schema is { } schema && !Supported(schema))
		{
			Form.BindErrors(new Dictionary<string, string> { ["_global"] = "Unsupported application schema: expected schema_version 1 and kind form or view." }, false, failed);
			return true;
		}
		if (result.Ok)
		{
			if (action.OnSuccess is { ResetFields.Any: true } success) Form.ResetValues(success.ResetFields);
			if (result.Fields is not null && (action.OnSuccess?.ResetFields.Any == true || action.OnSuccess?.MergeFields == true))
				Form.MergeFields(result.Fields);
		}
		if (result.Data is not null)
		{
			Data = result.Data;
			Form.ApplyData(result.Data);
		}
		// Old display data is not prefill for a reset: only this response's data is authoritative.
		if (result.Schema is not null || (result.Ok && action.OnSuccess?.ResetFields.Any == true))
			Form.Load(result.Schema ?? Document, null);
		if (!result.Ok) Form.BindErrors(result.Errors, action.OnError?.BindFieldErrors ?? true, failed);
		else
		{
			Toast = action.OnSuccess?.Toast ?? result.Message;
			var redirect = result.Redirect ?? action.OnSuccess?.Navigate;
			if (!string.IsNullOrWhiteSpace(redirect))
			{
				if (SchemaRendering.IsSafeUrl(redirect)) Redirect = redirect;
				else Form.GlobalError = "The action returned an unsafe redirect.";
			}
		}
		return true;
	}
}
