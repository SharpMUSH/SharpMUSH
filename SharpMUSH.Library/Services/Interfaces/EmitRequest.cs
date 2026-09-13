using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public enum EmitScope { Private, Prompt, Immediate, Room, Outermost, Omit, Zone }

/// <summary>Parsed emit intent. Targets are already split according to the calling surface's list
/// semantics; OmitLocation is the optional explicit container in an OEMIT location/list argument.
/// NoSpoof suppresses recipient tagging when permitted; Spoof attributes output to the enactor.
/// List retains refusal-message policy even for a one-element list. PortTargets selects the entire
/// private target list as descriptors; mixed descriptor-looking names and DBRefs remain objects.</summary>
public sealed record EmitRequest(
	EmitScope Scope,
	MString Message,
	IReadOnlyList<string> Targets,
	bool Silent = false,
	bool NoSpoof = false,
	bool Spoof = false,
	string? OmitLocation = null,
	bool List = false,
	bool PortTargets = false);

/// <summary>Admission is independent of recipient filtering: a permitted location counts even
/// when nobody hears it; private output requires a permitted target. Result retains the function
/// contract. TargetFailure preserves lookup failures for commands that otherwise return the payload.</summary>
public sealed record EmitOutcome(bool Admitted, CallState Result)
{
	public Option<CallState> TargetFailure { get; init; } = new None();
}
