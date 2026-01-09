# Parameter Names Implementation Guide

This guide explains how to add meaningful parameter names to all SharpMUSH functions and commands for enhanced IDE support (inlay hints, signature help, hover information).

## Background

Parameter names are stored in the `ParameterNames` property of `SharpFunctionAttribute` and `SharpCommandAttribute`. These names appear as inlay hints in IDEs, helping developers understand function parameters without consulting documentation.

## Naming Patterns

### 1. Fixed Parameters
For functions with a fixed number of parameters, use a simple string array:

```csharp
[SharpFunction(Name = "add", MinArgs = 2, MaxArgs = 2,
    ParameterNames = ["number1", "number2"])]
```

Results in inlay hints: `add(number1: 5, number2: 10)`

### 2. Variadic Parameters
For functions accepting unlimited arguments of the same type, use the `...` marker:

```csharp
[SharpFunction(Name = "max", MinArgs = 2, MaxArgs = int.MaxValue,
    ParameterNames = ["number..."])]
```

Results in inlay hints: `max(number1: 5, number2: 10, number3: 15)`

### 3. Paired Repeating Parameters
For functions with alternating parameter pairs (like switch statements), use the `|` separator:

```csharp
[SharpFunction(Name = "case", MinArgs = 3, MaxArgs = int.MaxValue,
    ParameterNames = ["expression", "case...|result...", "default"])]
```

Results in inlay hints: `case(expression: val, case1: "foo", result1: "bar", case2: "baz", result2: "qux", default: "none")`

### 4. Mixed Patterns
Complex functions can combine fixed parameters with variadic:

```csharp
[SharpFunction(Name = "speak", MinArgs = 2, MaxArgs = 7,
    ParameterNames = ["speaker", "string", "say-string", "transform-attr", "isnull-attr", "open", "close"])]
```

## Naming Conventions

### Source of Truth
Parameter names should match **PennMUSH help file documentation** exactly (without angle brackets):
- ✅ `"object/attribute"` (from help file: `<object>/<attribute>`)
- ✅ `"number"` (from help file: `<number>`)
- ✅ `"delimiter"` (from help file: `<delimiter>`)
- ❌ `"obj"` (abbreviation not in help)
- ❌ `"delim"` (abbreviation not in help)

### Naming Guidelines
- Use lowercase with hyphens for multi-word names: `"say-string"`, `"encoded-string"`
- Use descriptive names that indicate purpose: `"dividend"`, `"divisor"` not `"number1"`, `"number2"`
- For slash-separated references use exact help syntax: `"object/attribute"`, `"exit/destination"`
- For optional parameters, just include the name - optionality is inferred from MinArgs/MaxArgs

## Implementation Status

### Completed
- ✅ Infrastructure in place (SharpFunctionAttribute, SharpCommandAttribute, InlayHintHandler)
- ✅ ~15 functions with parameter names added as examples
- ✅ All three naming patterns demonstrated and tested

### Remaining Work

#### Functions (~385 remaining)
- **AttributeFunctions.cs** (~58 functions) - `get()`, `set()`, `eval()`, etc.
- **MathFunctions.cs** (~47 remaining) - `sin()`, `cos()`, `pow()`, `sqrt()`, etc.
- **StringFunctions.cs** (~65 remaining) - `strlen()`, `mid()`, `left()`, `right()`, etc.
- **ListFunctions.cs** (~50) - `first()`, `rest()`, `sort()`, `filter()`, etc.
- **UtilityFunctions.cs** (~46 remaining) - `benchmark()`, `clone()`, `create()`, etc.
- **DbrefFunctions.cs** (~62 remaining) - `owner()`, `loc()`, `flags()`, etc.
- **InformationFunctions.cs** (~31) - `version()`, `objid()`, `doing()`, etc.
- **ConnectionFunctions.cs** (~33) - `conn()`, `lwho()`, `idle()`, etc.
- **CommunicationFunctions.cs** (~15) - `pemit()`, `remit()`, `oemit()`, etc.
- **BooleanFunctions.cs** (~17) - `and()`, `or()`, `not()`, `xor()`, etc.
- **TimeFunctions.cs** (~17) - `time()`, `secs()`, `convsecs()`, etc.
- **RegExFunctions.cs** (~10 remaining) - `regmatch()`, `regreplace()`, etc.
- **ListFunctions.cs** (~50) - List manipulation functions
- **ChannelFunctions.cs** (~17) - Channel-related functions
- **ConnectionFunctions.cs** (~33) - Connection-related functions
- **MailFunctions.cs** (~11) - Mail system functions
- **BitwiseFunctions.cs** (~8) - Bitwise operations
- **SQLFunctions.cs** (~3) - SQL functions
- **JSONFunctions.cs** (~6) - JSON manipulation
- **HTMLFunctions.cs** (~6) - HTML functions
- **MarkdownFunctions.cs** (~2) - Markdown functions

#### Commands (~180 total)
- **GeneralCommands.cs** (~60) - `say`, `pose`, `@emit`, etc.
- **BuildingCommands.cs** (~19) - `@create`, `@dig`, `@open`, `@link`, etc.
- **MoreCommands.cs** (~37) - Various commands
- **WizardCommands.cs** (~33) - Admin commands
- **AttributeCommands.cs** (~5) - `@set`, `&`, etc.
- **ChannelCommands.cs** (~8) - Channel commands
- **DatabaseCommands.cs** (~2) - Database operations
- **HttpCommands.cs** (~2) - HTTP operations  
- **MetricsCommands.cs** (~1) - Metrics
- **SingleTokenCommands.cs** (~2) - Single-character commands
- **SocketCommands.cs** (~3) - Socket operations

## Step-by-Step Process

For each function or command:

1. **Find the help file** - Check `SharpMUSH.Documentation/Helpfiles/` for the function/command
2. **Extract parameter names** - Get exact parameter names from help syntax
3. **Determine pattern** - Fixed, variadic, or paired?
4. **Add ParameterNames** - Update the attribute with the names array
5. **Verify** - Build and test with a sample inlay hint

### Example Workflow

```bash
# 1. Find help file
cat SharpMUSH.Documentation/Helpfiles/Functions/get.txt
# Shows: get(<object>/<attribute>)

# 2. Add to function definition
[SharpFunction(Name = "get", MinArgs = 1, MaxArgs = 2,
    ParameterNames = ["object/attribute"])]

# 3. Build and verify
dotnet build SharpMUSH.LanguageServer
```

## Testing

After adding parameter names:

1. **Build** the LanguageServer project
2. **Start** the LSP server
3. **Open** a .mush file in your IDE
4. **Type** a function call: `get(#123/DESC)`
5. **Verify** inlay hints appear: `get(object/attribute: #123/DESC)`

## Priority Functions

Focus on the most commonly used functions first:

**High Priority:**
- get, set, eval, default, u - Attribute functions
- add, sub, mul, div, mod - Basic math
- strlen, mid, left, right, trim - String basics
- first, rest, elements, ldelete - List basics
- name, loc, owner, flags - Dbref basics
- pemit, remit, oemit - Communication

**Medium Priority:**
- All remaining math, string, and list functions
- Time and boolean functions
- Regex functions

**Lower Priority:**
- SQL, JSON, HTML functions (less commonly used)
- Specialized utility functions

## Automation Opportunities

For bulk updates, consider:
1. Parsing help files to extract parameter syntax
2. Generating ParameterNames arrays from help syntax
3. Batch updating multiple functions at once

However, manual review is recommended to ensure accuracy and handle edge cases.

## Questions?

If unsure about a parameter name:
- Consult the PennMUSH help files first
- Check existing implementations for similar functions
- Use descriptive names that match the function's purpose
- When in doubt, use the name from the original MUSH documentation
