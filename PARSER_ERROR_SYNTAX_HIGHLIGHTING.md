# Parser Error Explanations and Syntax Highlighting

This document describes the new parser features for better error reporting and syntax highlighting support.

## Overview

The SharpMUSH parser now supports:

1. **Configurable Prediction Mode**: Choose between SLL (faster) or LL (more powerful) parsing modes
2. **Detailed Error Explanations**: Get precise error information including position, expected tokens, and helpful messages
3. **Syntax Highlighting**: Tokenize MUSH code for syntax highlighting in editors or UIs

## Configuration

### Parser Prediction Mode

You can configure the parser prediction mode in your configuration file:

```conf
# Use SLL mode for faster parsing (default for simple grammars)
parser_prediction_mode = SLL

# Use LL mode for more powerful parsing (better for complex grammars)
parser_prediction_mode = LL
```

**Modes:**

- **SLL (Strong LL)**: Faster but less powerful. Good for most MUSH code.
- **LL (Full LL\*)**: Slower but more powerful. Handles complex nested structures better.

The default mode is **LL** for maximum compatibility.

## Features

### 1. Error Validation with Detailed Explanations

Use `ValidateAndGetErrors()` to check MUSH code for syntax errors:

```csharp
var parser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
var errors = parser.ValidateAndGetErrors(
    MModule.single("add(1,2"),  // Missing closing parenthesis
    ParseType.Function
);

if (errors.Count > 0)
{
    foreach (var error in errors)
    {
        Console.WriteLine($"Error at line {error.Line}, column {error.Column}:");
        Console.WriteLine($"  {error.Message}");
        
        if (error.OffendingToken != null)
        {
            Console.WriteLine($"  Unexpected token: '{error.OffendingToken}'");
        }
        
        if (error.ExpectedTokens != null)
        {
            Console.WriteLine($"  Expected: {string.Join(", ", error.ExpectedTokens)}");
        }
    }
}
```

**Output:**
```
Error at line 1, column 7:
  Unexpected end of input - missing closing delimiter?
  Expected: ')'
```

**Parse Types:**
- `ParseType.Function` - Function/expression parsing
- `ParseType.Command` - Single command parsing
- `ParseType.CommandList` - Command list parsing (semicolon-separated)
- `ParseType.CommandSingleArg` - Single argument parsing
- `ParseType.CommandCommaArgs` - Comma-separated arguments
- `ParseType.CommandEqSplitArgs` - Arguments with `=` split
- `ParseType.CommandEqSplit` - Command with `=` split

### 2. Syntax Highlighting via Tokenization

Use `Tokenize()` to get token information for syntax highlighting:

```csharp
var parser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
var tokens = parser.Tokenize(MModule.single("add(1,2)[sub(5,3)]"));

foreach (var token in tokens)
{
    Console.WriteLine($"{token.Type}: '{token.Text}' at {token.StartIndex}-{token.EndIndex}");
}
```

**Output:**
```
FUNCHAR: 'add(' at 0-3
OTHER: '1' at 4-4
COMMAWS: ',' at 5-5
OTHER: '2' at 6-6
CPAREN: ')' at 7-7
OBRACK: '[' at 8-8
FUNCHAR: 'sub(' at 9-12
OTHER: '5' at 13-13
COMMAWS: ',' at 14-14
OTHER: '3' at 15-15
CPAREN: ')' at 16-16
CBRACK: ']' at 17-17
```

**Token Types:**

- `FUNCHAR` - Function name with opening parenthesis (e.g., `add(`)
- `OBRACK` / `CBRACK` - Opening/closing brackets `[` / `]`
- `OBRACE` / `CBRACE` - Opening/closing braces `{` / `}`
- `OPAREN` / `CPAREN` - Opening/closing parentheses `(` / `)`
- `COMMAWS` - Comma with whitespace
- `EQUALS` - Equals sign with whitespace
- `PERCENT` - Percent sign `%` (substitution)
- `SEMICOLON` - Semicolon (command separator)
- `ESCAPE` - Backslash escape `\`
- `OTHER` - Regular text
- And many more (see `SharpMUSHLexer.g4` for complete list)

**TokenInfo Properties:**

- `Type` - The token type name
- `Text` - The actual text of the token
- `StartIndex` - Start position in input (0-based)
- `EndIndex` - End position in input (0-based, inclusive)
- `Line` - Line number (1-based)
- `Column` - Column position (0-based)
- `Length` - Length of the token
- `Channel` - Token channel (usually 0)

### 3. Example: Syntax Highlighting in a UI

```csharp
public class SyntaxHighlighter
{
    private readonly IMUSHCodeParser _parser;
    
    public string HighlightMUSHCode(string input)
    {
        var tokens = _parser.Tokenize(MModule.single(input));
        var sb = new StringBuilder();
        
        foreach (var token in tokens)
        {
            var cssClass = GetCssClassForToken(token.Type);
            sb.Append($"<span class='{cssClass}'>{HttpUtility.HtmlEncode(token.Text)}</span>");
        }
        
        return sb.ToString();
    }
    
    private string GetCssClassForToken(string tokenType)
    {
        return tokenType switch
        {
            "FUNCHAR" => "mush-function",
            "OBRACK" or "CBRACK" => "mush-bracket",
            "OBRACE" or "CBRACE" => "mush-brace",
            "PERCENT" => "mush-substitution",
            "ESCAPE" => "mush-escape",
            "COMMAWS" or "EQUALS" => "mush-operator",
            "SEMICOLON" => "mush-separator",
            _ => "mush-text"
        };
    }
}
```

## Implementation Details

### ParseError Model

```csharp
public record ParseError
{
    public int Line { get; init; }
    public int Column { get; init; }
    public required string Message { get; init; }
    public string? OffendingToken { get; init; }
    public IReadOnlyList<string>? ExpectedTokens { get; init; }
    public string? InputText { get; init; }
}
```

### TokenInfo Model

```csharp
public record TokenInfo
{
    public required string Type { get; init; }
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public required string Text { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int Channel { get; init; }
    public int Length => EndIndex - StartIndex + 1;
}
```

### ParserErrorListener

The `ParserErrorListener` class collects syntax errors during parsing and provides:

- Enhanced error messages
- Token position information
- Expected token suggestions
- Input text context

## Performance Considerations

### SLL vs LL Mode

- **SLL Mode**: 
  - ~10-30% faster than LL mode
  - Suitable for most MUSH code
  - May fail on highly ambiguous grammars
  - Falls back to standard ANTLR error recovery

- **LL Mode**:
  - More powerful and can handle complex nested structures
  - Better error recovery and reporting
  - Default mode for maximum compatibility
  - Slightly slower but more robust

### When to Use Each Mode

**Use SLL when:**
- Performance is critical
- Your MUSH code is relatively simple
- You're parsing large volumes of code

**Use LL when:**
- You need better error messages
- Your code has complex nested structures
- Accuracy is more important than speed
- You're providing an interactive editor/IDE

## Examples

See `SharpMUSH.Tests/Parser/ParserExamples.cs` for complete working examples of:

- Error validation and reporting
- Syntax highlighting with token types
- Comparing different token patterns

## Future Enhancements

Potential future improvements:

1. **Configurable Error Recovery**: Allow customization of how the parser recovers from errors
2. **Error Suggestions**: Provide fix suggestions for common errors
3. **Incremental Parsing**: Parse only changed portions for better editor performance
4. **Semantic Highlighting**: Add semantic token information (variables, functions, etc.)
5. **Error Ranges**: Provide start/end ranges for multi-token errors

## API Reference

### IMUSHCodeParser Interface

```csharp
public interface IMUSHCodeParser
{
    // ... existing methods ...
    
    /// <summary>
    /// Tokenizes the input text and returns token information for syntax highlighting.
    /// </summary>
    IReadOnlyList<TokenInfo> Tokenize(MString text);
    
    /// <summary>
    /// Parses the input text and returns any errors encountered.
    /// Uses the configured prediction mode (SLL or LL) for parsing.
    /// </summary>
    IReadOnlyList<ParseError> ValidateAndGetErrors(
        MString text, 
        ParseType parseType = ParseType.Function
    );
}
```

### ParseType Enum

```csharp
public enum ParseType
{
    Function,           // Function/expression parsing
    Command,            // Single command
    CommandList,        // Semicolon-separated commands
    CommandSingleArg,   // Single argument
    CommandCommaArgs,   // Comma-separated arguments
    CommandEqSplitArgs, // Arguments with = split
    CommandEqSplit      // Command with = split
}
```

### ParserPredictionMode Enum

```csharp
public enum ParserPredictionMode
{
    SLL,  // Strong LL - faster
    LL    // Full LL(*) - more powerful (default)
}
```
