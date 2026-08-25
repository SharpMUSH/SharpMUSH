using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IAttributeService
{
	enum AttributeMode
	{
		Read = 0,
		Execute = 1,
		Set = 2,
		SystemSet = 3
	}

	enum AttributePatternMode
	{
		Exact = 0,
		Wildcard = 1,
		Regex = 2
	}

	ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributeMode mode, bool parent = true);

	ValueTask<OptionalLazySharpAttributeOrError> LazilyGetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributeMode mode, bool parent = true);

	ValueTask<OneOf<Success, Error<string>>> SetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, MString value);

	ValueTask<OneOf<Success, Error<string>>> ClearAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributePatternMode patternMode);

	ValueTask<LazySharpAttributesOrError> LazilyGetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj, int depth = 1);

	ValueTask<LazySharpAttributesOrError> LazilyGetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj,
		string attributePattern, bool checkParents, AttributePatternMode mode = AttributePatternMode.Exact);

	ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj, int depth = 1);

	ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern, bool checkParents, AttributePatternMode mode = AttributePatternMode.Exact);

	ValueTask<OneOf<Success, Error<string>>> SetAttributeFlagAsync(AnySharpObject executor, AnySharpObject obj, string attribute, string flag);

	ValueTask<OneOf<Success, Error<string>>> UnsetAttributeFlagAsync(AnySharpObject executor, AnySharpObject obj, string attribute, string flag);

	/// <summary>
	/// Applies a whole list of <c>!</c>-prefixable flag tokens to one attribute as a single
	/// operation - one permission check against the pre-batch state, covering every token.
	/// See the implementation's remarks for why this must not be a loop of single-flag calls.
	/// </summary>
	ValueTask<OneOf<Success, Error<string>>> SetAttributeFlagsAsync(AnySharpObject executor, AnySharpObject obj, string attribute, IReadOnlyList<string> flagTokens);

	ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject obj,
		string attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false);

	ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor, MString objAndAttribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false, bool ignoreLambda = false);

	/// <summary>
	/// Mirrors PennMUSH's <c>do_parent</c> <c>MAX_PARENTS</c> guard (<c>src/set.c:1442-1446</c>): true
	/// when <paramref name="prospectiveParent"/> already has at least <c>Limit.MaxParents</c> ancestors
	/// above it, meaning attaching a child under it would grow the child's own chain past the cap.
	/// This is independent of cycle detection (<see cref="HelperFunctions.SafeToAddParent"/>) - a
	/// caller wiring up <c>@parent</c> needs both checks.
	/// </summary>
	ValueTask<bool> ExceedsMaxParentDepthAsync(AnySharpObject prospectiveParent, CancellationToken cancellationToken = default);
}