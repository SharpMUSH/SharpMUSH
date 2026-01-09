# LSP-Compatible Semantic Highlighting and Error Ranges

This document describes the LSP-compatible semantic highlighting and multi-token error range features added to the SharpMUSH parser.

## Overview

Building on the parser error explanations and syntax highlighting features, we've added:

1. **LSP-Compatible Data Structures**: Position, Range, and Diagnostic types that align with the Language Server Protocol specification
2. **Semantic Token Analysis**: Goes beyond syntax highlighting to understand the meaning of code elements
3. **Multi-Token Error Ranges**: Errors now span ranges of tokens, not just single positions
4. **LSP Delta Encoding**: Efficient transmission format for semantic tokens

These features provide a solid foundation for LSP integration while being useful standalone.

## LSP-Compatible Models

### Position

Represents a zero-based position in a document (line and character).

```csharp
public record Position
{
    public int Line { get; init; }        // 0-based line number
    public int Character { get; init; }   // 0-based character offset
    
    public bool IsBefore(Position other);
    public bool IsAfter(Position other);
    public bool IsBeforeOrEqual(Position other);
    public bool IsAfterOrEqual(Position other);
}
```

**Example:**
```csharp
var pos = new Position(line: 0, character: 5);  // Line 1, column 6 (0-based)
```

### Range

Represents a range in a document with start and end positions (end is exclusive).

```csharp
public record Range
{
    public required Position Start { get; init; }
    public required Position End { get; init; }    // Exclusive
    
    public bool IsEmpty { get; }
    public bool IsSingleLine { get; }
    public bool Contains(Position position);
    public bool Contains(Range other);
    public bool Intersects(Range other);
}
```

**Example:**
```csharp
var range = new Range
{
    Start = new Position(0, 5),
    End = new Position(0, 10)  // Characters 5-9 (10 is exclusive)
};

var pos = new Position(0, 7);
if (range.Contains(pos))
{
    Console.WriteLine("Position is within range");
}
```

### Diagnostic

LSP-compatible diagnostic information for errors, warnings, and hints.

```csharp
public record Diagnostic
{
    public required Range Range { get; init; }
    public DiagnosticSeverity Severity { get; init; }  // Error, Warning, Information, Hint
    public string? Code { get; init; }
    public string? Source { get; init; }
    public required string Message { get; init; }
    public DiagnosticTag[]? Tags { get; init; }
    public DiagnosticRelatedInformation[]? RelatedInformation { get; init; }
    public string? OffendingToken { get; init; }
    public IReadOnlyList<string>? ExpectedTokens { get; init; }
}
```

**Severity Levels:**
- `Error` (1): Reports an error
- `Warning` (2): Reports a warning
- `Information` (3): Reports information
- `Hint` (4): Reports a hint

**Tags:**
- `Unnecessary`: Unused or unnecessary code (can be faded in editors)
- `Deprecated`: Deprecated or obsolete code (can be struck through)

**Example:**
```csharp
var diagnostics = parser.GetDiagnostics(MModule.single("add(1,2"), ParseType.Function);

foreach (var diagnostic in diagnostics)
{
    Console.WriteLine($"{diagnostic.Severity} at {diagnostic.Range}: {diagnostic.Message}");
    if (diagnostic.ExpectedTokens != null)
    {
        Console.WriteLine($"  Expected: {string.Join(", ", diagnostic.ExpectedTokens)}");
    }
}
```

**Output:**
```
Error at [(0, 7) - (0, 7)): Unexpected end of input - missing closing delimiter?
  Expected: ')'
```

## Semantic Tokens

### SemanticTokenType

Identifies the semantic meaning of code elements, going beyond syntax.

**Standard LSP Types:**
- `Namespace`, `Class`, `Enum`, `Interface`, `Struct`, `Type`
- `Parameter`, `Variable`, `Property`, `Function`, `Method`
- `Keyword`, `String`, `Number`, `Comment`, `Operator`

**MUSH-Specific Types:**
- `ObjectReference`: MUSH object references (e.g., `#123`, `%#`)
- `AttributeReference`: Attribute names
- `Substitution`: Substitution tokens (e.g., `%0`, `%N`)
- `Register`: Register references (e.g., `%q<register>`, `%va`)
- `Command`: MUSH command names (e.g., `@emit`, `say`)
- `EscapeSequence`: Escape sequences (e.g., `\n`, `\t`)
- `AnsiCode`: ANSI color codes
- `BracketSubstitution`: Bracket substitutions `[...]`
- `BraceGroup`: Brace groupings `{...}`
- `UserFunction`: User-defined functions
- `Flag`, `Power`: Flag and power references
- `Text`: Regular text

### SemanticTokenModifier

Modifiers that provide additional context about tokens.

```csharp
[Flags]
public enum SemanticTokenModifier
{
    None = 0,
    Declaration = 1 << 0,      // Symbol declarations
    Definition = 1 << 1,       // Symbol definitions
    Readonly = 1 << 2,         // Read-only symbols
    Static = 1 << 3,           // Static members
    Deprecated = 1 << 4,       // Deprecated symbols
    Abstract = 1 << 5,         // Abstract symbols
    Async = 1 << 6,            // Async functions
    Modification = 1 << 7,     // Modification context
    Documentation = 1 << 8,    // Documentation occurrences
    DefaultLibrary = 1 << 9    // Standard library symbols
}
```

**Example Usage:**
```csharp
var modifier = SemanticTokenModifier.DefaultLibrary | SemanticTokenModifier.Readonly;
```

### SemanticToken

Represents a single semantic token with its type, modifiers, and range.

```csharp
public record SemanticToken
{
    public required Range Range { get; init; }
    public SemanticTokenType TokenType { get; init; }
    public SemanticTokenModifier Modifiers { get; init; }
    public required string Text { get; init; }
    public object? Data { get; init; }  // Additional type-specific data
    public int Length => Text.Length;
}
```

**Example:**
```csharp
var tokens = parser.GetSemanticTokens(MModule.single("add(1,2)"), ParseType.Function);

foreach (var token in tokens)
{
    Console.WriteLine($"{token.TokenType}: '{token.Text}' at {token.Range}");
    if (token.Modifiers != SemanticTokenModifier.None)
    {
        Console.WriteLine($"  Modifiers: {token.Modifiers}");
    }
}
```

**Output:**
```
Function: 'add(' at [(0, 0) - (0, 4))
  Modifiers: DefaultLibrary
Number: '1' at [(0, 4) - (0, 5))
Operator: ',' at [(0, 5) - (0, 6))
Number: '2' at [(0, 6) - (0, 7))
Operator: ')' at [(0, 7) - (0, 8))
```

### SemanticTokensData

LSP delta-encoded format for efficient transmission.

```csharp
public record SemanticTokensData
{
    public required string[] TokenTypes { get; init; }
    public required string[] TokenModifiers { get; init; }
    public required int[] Data { get; init; }  // Delta-encoded: [deltaLine, deltaChar, length, tokenType, modifiers]
    
    public static SemanticTokensData FromTokens(IReadOnlyList<SemanticToken> tokens);
}
```

The `Data` array contains groups of 5 integers for each token:
1. **deltaLine**: Line delta from previous token (absolute for first token)
2. **deltaChar**: Character delta from previous token (absolute for first token on new line)
3. **length**: Token length in characters
4. **tokenType**: Index into `TokenTypes` array
5. **tokenModifiers**: Bit flags representing indices in `TokenModifiers` array

**Example:**
```csharp
var data = parser.GetSemanticTokensData(MModule.single("add(1,2)"), ParseType.Function);

Console.WriteLine($"Token Types: {string.Join(", ", data.TokenTypes)}");
Console.WriteLine($"Token Modifiers: {string.Join(", ", data.TokenModifiers)}");
Console.WriteLine($"Data length: {data.Data.Length} (= {data.Data.Length / 5} tokens)");

// Decode first token
int i = 0;
Console.WriteLine($"Token 0: line={data.Data[i]}, char={data.Data[i+1]}, " +
                 $"len={data.Data[i+2]}, type={data.TokenTypes[data.Data[i+3]]}, " +
                 $"mods={data.Data[i+4]}");
```

## API Reference

### IMUSHCodeParser Interface

```csharp
public interface IMUSHCodeParser
{
    // ... existing methods ...
    
    /// <summary>
    /// Parses input and returns diagnostics (LSP-compatible errors/warnings).
    /// Includes error ranges spanning multiple tokens.
    /// </summary>
    IReadOnlyList<Diagnostic> GetDiagnostics(MString text, ParseType parseType = ParseType.Function);
    
    /// <summary>
    /// Performs semantic analysis and returns semantic tokens.
    /// </summary>
    IReadOnlyList<SemanticToken> GetSemanticTokens(MString text, ParseType parseType = ParseType.Function);
    
    /// <summary>
    /// Performs semantic analysis and returns tokens in LSP delta-encoded format.
    /// </summary>
    SemanticTokensData GetSemanticTokensData(MString text, ParseType parseType = ParseType.Function);
}
```

### Enhanced ParseError

```csharp
public record ParseError
{
    public int Line { get; init; }              // 1-based for backward compatibility
    public int Column { get; init; }            // 0-based
    public Range? Range { get; init; }          // LSP-compatible range (NEW)
    public required string Message { get; init; }
    public string? OffendingToken { get; init; }
    public IReadOnlyList<string>? ExpectedTokens { get; init; }
    public string? InputText { get; init; }
    
    /// <summary>
    /// Converts to LSP-compatible Diagnostic.
    /// </summary>
    public Diagnostic ToDiagnostic();
}
```

## Usage Examples

### Example 1: Error Diagnostics with Ranges

```csharp
var parser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
var diagnostics = parser.GetDiagnostics(
    MModule.single("add(1,2[sub(3,4)"),  // Missing closing bracket and paren
    ParseType.Function
);

foreach (var diagnostic in diagnostics)
{
    // Highlight the error range in editor
    HighlightRange(diagnostic.Range, diagnostic.Severity);
    
    // Show error message
    ShowError(diagnostic.Message, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
    
    // Show expected tokens as suggestions
    if (diagnostic.ExpectedTokens?.Count > 0)
    {
        ShowQuickFix($"Add {diagnostic.ExpectedTokens[0]}");
    }
}
```

### Example 2: Semantic Highlighting in Editor

```csharp
var tokens = parser.GetSemanticTokens(
    MModule.single("add(%#,get(name))"),
    ParseType.Function
);

foreach (var token in tokens)
{
    var color = GetColorForSemanticType(token.TokenType);
    var fontStyle = GetStyleForModifiers(token.Modifiers);
    
    HighlightText(
        token.Range.Start.Line,
        token.Range.Start.Character,
        token.Length,
        color,
        fontStyle
    );
}

Color GetColorForSemanticType(SemanticTokenType type)
{
    return type switch
    {
        SemanticTokenType.Function => Colors.Blue,
        SemanticTokenType.ObjectReference => Colors.Purple,
        SemanticTokenType.Substitution => Colors.Green,
        SemanticTokenType.Number => Colors.DarkCyan,
        SemanticTokenType.Operator => Colors.Gray,
        _ => Colors.White
    };
}

FontStyle GetStyleForModifiers(SemanticTokenModifier modifiers)
{
    if (modifiers.HasFlag(SemanticTokenModifier.Deprecated))
        return FontStyle.Strikethrough;
    if (modifiers.HasFlag(SemanticTokenModifier.DefaultLibrary))
        return FontStyle.Bold;
    return FontStyle.Regular;
}
```

### Example 3: LSP Server Integration

```csharp
// In your LSP server's textDocument/semanticTokens/full handler
public SemanticTokens GetSemanticTokens(TextDocumentIdentifier document)
{
    var text = GetDocumentText(document.Uri);
    var data = parser.GetSemanticTokensData(MModule.single(text), ParseType.Function);
    
    return new SemanticTokens
    {
        Data = data.Data
    };
}

// In your LSP server's initialize handler
public InitializeResult Initialize(InitializeParams @params)
{
    return new InitializeResult
    {
        Capabilities = new ServerCapabilities
        {
            SemanticTokensProvider = new SemanticTokensOptions
            {
                Legend = new SemanticTokensLegend
                {
                    TokenTypes = data.TokenTypes,
                    TokenModifiers = data.TokenModifiers
                },
                Full = true
            },
            DiagnosticProvider = new DiagnosticOptions
            {
                InterFileDependencies = false,
                WorkspaceDiagnostics = false
            }
        }
    };
}

// In your LSP server's textDocument/diagnostic handler
public Diagnostic[] GetDiagnostics(TextDocumentIdentifier document)
{
    var text = GetDocumentText(document.Uri);
    var diagnostics = parser.GetDiagnostics(MModule.single(text), ParseType.Function);
    
    return diagnostics.Select(d => new Diagnostic
    {
        Range = new LSP.Range
        {
            Start = new Position(d.Range.Start.Line, d.Range.Start.Character),
            End = new Position(d.Range.End.Line, d.Range.End.Character)
        },
        Severity = (DiagnosticSeverity)(int)d.Severity,
        Source = d.Source,
        Message = d.Message,
        Code = d.Code
    }).ToArray();
}
```

### Example 4: Multi-Token Error Ranges

```csharp
var diagnostics = parser.GetDiagnostics(
    MModule.single("add(1,2[unclosed"),
    ParseType.Function
);

// Error range spans multiple tokens
var error = diagnostics[0];
Console.WriteLine($"Error range: {error.Range}");
// Output: Error range: [(0, 7) - (0, 16))

// The range spans from '[' to 'unclosed'
// Can be highlighted in editor to show the entire problematic section
```

### Example 5: Differentiating Token Semantics

```csharp
var tokens = parser.GetSemanticTokens(
    MModule.single("get(%#,name) u(myfunc,value)"),
    ParseType.Function
);

// Find all function calls
var functionCalls = tokens.Where(t => t.TokenType == SemanticTokenType.Function).ToList();
Console.WriteLine($"Found {functionCalls.Count} function calls");

// Distinguish built-in vs user functions
var builtInFunctions = tokens.Where(t => 
    t.TokenType == SemanticTokenType.Function &&
    t.Modifiers.HasFlag(SemanticTokenModifier.DefaultLibrary)
).ToList();

var userFunctions = tokens.Where(t => 
    t.TokenType == SemanticTokenType.UserFunction
).ToList();

Console.WriteLine($"Built-in: {builtInFunctions.Count}, User-defined: {userFunctions.Count}");
```

## Semantic Token Classification

The parser performs intelligent classification of tokens:

### Functions
- Checks against `FunctionLibrary` to determine if built-in or user-defined
- Built-in functions get `DefaultLibrary` modifier
- User functions get `UserFunction` type

### Object References
- `#123` format: `ObjectReference`
- `%#`, `%!`, `%@` substitutions: `ObjectReference`

### Substitutions
- `%0`-`%9`: Argument substitutions (`Register`)
- `%N`, `%n`, `~`: Name substitutions (`Substitution`)
- `%q<reg>`, `%v<var>`: Register references (`Register`)

### Numbers
- Integer and decimal literals: `Number`

### Operators
- `=`, `,`, `;`, `>`: `Operator`

### Special Constructs
- `[...]`: `BracketSubstitution`
- `{...}`: `BraceGroup`
- `\n`, `\t`: `EscapeSequence`
- ANSI codes: `AnsiCode`

## LSP Integration Checklist

For full LSP integration, implement these handlers:

- [x] `Position` model (zero-based, LSP-compatible)
- [x] `Range` model (start/end positions, end exclusive)
- [x] `Diagnostic` model (errors, warnings, hints)
- [x] `SemanticToken` with types and modifiers
- [x] `SemanticTokensData` with delta encoding
- [ ] `textDocument/semanticTokens/full` handler
- [ ] `textDocument/semanticTokens/range` handler
- [ ] `textDocument/diagnostic` handler
- [ ] `textDocument/publishDiagnostics` notification
- [ ] `textDocument/codeAction` for quick fixes
- [ ] `textDocument/hover` for token information
- [ ] `textDocument/definition` for go-to-definition
- [ ] `textDocument/references` for find-all-references

## Performance Considerations

### Semantic Analysis
- Performed during parsing pass
- Uses existing parse tree
- Minimal overhead over syntax highlighting
- Results can be cached per document version

### Delta Encoding
- Reduces data size by ~40-60% compared to absolute positions
- Efficient for network transmission in LSP
- Can be incrementally updated for edits

### Range Calculation
- Computed during error detection
- No additional parsing pass needed
- Includes both single-position and multi-token ranges

## Backward Compatibility

All existing APIs continue to work:
- `ValidateAndGetErrors()` returns `ParseError` list (with optional `Range` property)
- `Tokenize()` returns `TokenInfo` list (syntax tokens)
- New methods (`GetDiagnostics`, `GetSemanticTokens`) are additions, not replacements

`ParseError` can be converted to `Diagnostic`:
```csharp
var errors = parser.ValidateAndGetErrors(text);
var diagnostics = errors.Select(e => e.ToDiagnostic()).ToList();
```

## Testing

Comprehensive test coverage includes:
- `SemanticHighlightingTests`: 10 tests for semantic token extraction
- `DiagnosticTests`: 11 tests for LSP-compatible diagnostics and ranges
- All tests verify LSP-compatible data structures
- Range containment, intersection, and comparison operations tested
- Delta encoding format validated

## Future Enhancements

Planned improvements for full LSP support:

1. **Incremental Semantic Tokens**: Update only changed regions
2. **Semantic Token Ranges**: Request tokens for specific ranges
3. **Code Actions**: Quick fixes for common errors
4. **Hover Information**: Show token details on hover
5. **Symbol Information**: Document symbols, workspace symbols
6. **Go to Definition**: Navigate to symbol definitions
7. **Find All References**: Find all uses of a symbol
8. **Rename**: Rename symbols across files
9. **Signature Help**: Parameter hints for functions
10. **Completion**: Context-aware code completion

## Documentation

- This file: LSP integration guide
- `PARSER_ERROR_SYNTAX_HIGHLIGHTING.md`: Original syntax highlighting docs
- `IMPLEMENTATION_SUMMARY.md`: Implementation details
- Inline code documentation: All public APIs documented
- Test files: Usage examples in test code

## Summary

This enhancement provides:
- **LSP-compatible data structures** for seamless integration
- **Semantic highlighting** that understands code meaning
- **Multi-token error ranges** for better error visualization
- **Delta-encoded transmission** for efficiency
- **Full backward compatibility** with existing APIs

The implementation is ready for LSP server integration while being useful standalone for any MUSH code analysis or editing tool.
