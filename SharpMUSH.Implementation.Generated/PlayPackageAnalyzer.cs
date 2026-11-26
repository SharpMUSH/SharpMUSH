using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpMUSH.Implementation.Generated;

/// <summary>
/// SHARPMUSH001: an application that names one of the engine's own play packages (<c>sharp.*</c>) in
/// <c>RegisteredApplication.Subscribes</c> or <c>PlaySubscription.Packages</c>. It works, but every subscription
/// already gets what <c>sharp.play</c> says as <c>IPlaySession.State</c>, <c>PlayStateChanged</c> and
/// <c>PlayReset</c>, which keep to the plugin contract where the package's JSON need not.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PlayPackageAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "SHARPMUSH001";

	private static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		"Read the play state instead of the engine's packages",
		"'{0}' is one of the engine's own packages; read IPlaySession.State and the PlayStateChanged and PlayReset events instead",
		"Usage",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Every play subscription gets what sharp.play says as PlayState, PlayStateChanged and PlayReset. "
			+ "Those are part of the plugin contract; the package's JSON is the engine's wire format and may change.");

	private const string EnginePrefix = "sharp.";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterOperationAction(AnalyzeAssignment, OperationKind.SimpleAssignment);
	}

	private static void AnalyzeAssignment(OperationAnalysisContext context)
	{
		var assignment = (ISimpleAssignmentOperation)context.Operation;
		if (assignment.Target is not IPropertyReferenceOperation { Property: var property } || !IsPackageList(property)) return;

		foreach (var literal in assignment.Value.DescendantsAndSelf().OfType<ILiteralOperation>())
		{
			if (literal.ConstantValue is { HasValue: true, Value: string package }
				&& package.StartsWith(EnginePrefix, StringComparison.OrdinalIgnoreCase))
			{
				context.ReportDiagnostic(Diagnostic.Create(Rule, literal.Syntax.GetLocation(), package));
			}
		}
	}

	/// <summary>The two properties that list the play packages an application reads.</summary>
	private static bool IsPackageList(IPropertySymbol property) =>
		(property.Name, property.ContainingType?.Name) is ("Subscribes", "RegisteredApplication") or ("Packages", "PlaySubscription")
		&& property.ContainingNamespace?.ToDisplayString().StartsWith("SharpMUSH.", StringComparison.Ordinal) == true;
}
