namespace SharpMUSH.Library.Models;

/// <summary>
/// Semantic token types for MUSH code.
/// Based on LSP semantic token types with MUSH-specific extensions.
/// </summary>
public enum SemanticTokenType
{
	// Standard LSP semantic token types
	/// <summary>
	/// For namespace declarations and references.
	/// </summary>
	Namespace,

	/// <summary>
	/// For class type definitions and references.
	/// </summary>
	Class,

	/// <summary>
	/// For enum type definitions and references.
	/// </summary>
	Enum,

	/// <summary>
	/// For interface type definitions and references.
	/// </summary>
	Interface,

	/// <summary>
	/// For struct type definitions and references.
	/// </summary>
	Struct,

	/// <summary>
	/// For type parameter definitions and references.
	/// </summary>
	TypeParameter,

	/// <summary>
	/// For type definitions and references (not otherwise classified).
	/// </summary>
	Type,

	/// <summary>
	/// For parameter definitions and references.
	/// </summary>
	Parameter,

	/// <summary>
	/// For variable definitions and references.
	/// </summary>
	Variable,

	/// <summary>
	/// For property definitions and references.
	/// </summary>
	Property,

	/// <summary>
	/// For enum member definitions and references.
	/// </summary>
	EnumMember,

	/// <summary>
	/// For decorator or attribute definitions and references.
	/// </summary>
	Decorator,

	/// <summary>
	/// For event definitions and references.
	/// </summary>
	Event,

	/// <summary>
	/// For function definitions and references.
	/// In MUSH: built-in functions like add(), sub(), strcat(), etc.
	/// </summary>
	Function,

	/// <summary>
	/// For method definitions and references.
	/// </summary>
	Method,

	/// <summary>
	/// For macro definitions and references.
	/// In MUSH: could be used for command aliases or user-defined shortcuts.
	/// </summary>
	Macro,

	/// <summary>
	/// For label definitions and references.
	/// </summary>
	Label,

	/// <summary>
	/// For comment tokens.
	/// </summary>
	Comment,

	/// <summary>
	/// For string literals.
	/// </summary>
	String,

	/// <summary>
	/// For keyword tokens.
	/// </summary>
	Keyword,

	/// <summary>
	/// For number literals.
	/// </summary>
	Number,

	/// <summary>
	/// For regular expression literals.
	/// </summary>
	Regexp,

	/// <summary>
	/// For operator tokens.
	/// In MUSH: =, ,, ;, etc.
	/// </summary>
	Operator,

	// MUSH-specific semantic token types

	/// <summary>
	/// For MUSH object references (dbrefs like #123).
	/// </summary>
	ObjectReference,

	/// <summary>
	/// For MUSH attribute references (e.g., ATTRIBUTE`NAME or in get() calls).
	/// </summary>
	AttributeReference,

	/// <summary>
	/// For MUSH substitution tokens (e.g., %0, %1, %N, %#, etc.).
	/// </summary>
	Substitution,

	/// <summary>
	/// For MUSH register references (e.g., %q&lt;register&gt;, %va).
	/// </summary>
	Register,

	/// <summary>
	/// For MUSH command names (e.g., @emit, @create, say, etc.).
	/// </summary>
	Command,

	/// <summary>
	/// For escape sequences (e.g., \n, \t, \\, etc.).
	/// </summary>
	EscapeSequence,

	/// <summary>
	/// For ANSI color codes.
	/// </summary>
	AnsiCode,

	/// <summary>
	/// For bracket substitutions [function()].
	/// </summary>
	BracketSubstitution,

	/// <summary>
	/// For brace groupings {text}.
	/// </summary>
	BraceGroup,

	/// <summary>
	/// For user-defined functions (u-functions).
	/// </summary>
	UserFunction,

	/// <summary>
	/// For flag references (e.g., in flag checks).
	/// </summary>
	Flag,

	/// <summary>
	/// For power references.
	/// </summary>
	Power,

	/// <summary>
	/// For regular text (not otherwise classified).
	/// </summary>
	Text
}

/// <summary>
/// Semantic token modifiers for MUSH code.
/// Based on LSP semantic token modifiers.
/// </summary>
[Flags]
public enum SemanticTokenModifier
{
	/// <summary>
	/// No modifiers.
	/// </summary>
	None = 0,

	/// <summary>
	/// For declarations of symbols.
	/// </summary>
	Declaration = 1 << 0,

	/// <summary>
	/// For definitions of symbols.
	/// </summary>
	Definition = 1 << 1,

	/// <summary>
	/// For readonly variables and member fields (constants).
	/// </summary>
	Readonly = 1 << 2,

	/// <summary>
	/// For class members (static members).
	/// </summary>
	Static = 1 << 3,

	/// <summary>
	/// For symbols that are deprecated.
	/// </summary>
	Deprecated = 1 << 4,

	/// <summary>
	/// For symbols that are abstract.
	/// </summary>
	Abstract = 1 << 5,

	/// <summary>
	/// For functions that are marked async.
	/// </summary>
	Async = 1 << 6,

	/// <summary>
	/// For symbols that are in modification context.
	/// </summary>
	Modification = 1 << 7,

	/// <summary>
	/// For occurrences of symbols in documentation.
	/// </summary>
	Documentation = 1 << 8,

	/// <summary>
	/// For symbols that are part of the standard library.
	/// In MUSH: built-in functions, commands, etc.
	/// </summary>
	DefaultLibrary = 1 << 9
}
