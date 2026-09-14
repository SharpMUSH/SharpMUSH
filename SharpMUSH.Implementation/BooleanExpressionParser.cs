using Antlr4.Runtime.Tree;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(
	ILockEvaluationServices services,
	IMediator mediator,
	[FromKeyedServices("compiled-expressions")] IFusionCache cache) : IBooleanExpressionParser
{
	// Keep in sync with the registration constant in Startup.cs (CompiledExpressionsCacheName).
	private const string CompiledExpressionsCacheName = "compiled-expressions";
	private const string CompiledExpressionsTag = "compiled-lock-expressions";
	private const string CacheKeyPrefix = "compiled-lock-expr:";

	/// <summary>
	/// Per-entry cache options for compiled lock expressions.
	/// <list type="bullet">
	///   <item><description><b>Duration 1 h</b> – absolute ceiling before eviction.</description></item>
	///   <item><description><b>EagerRefreshThreshold 0.9</b> – when an entry reaches 90 % of its TTL
	///     and is accessed, FusionCache silently recompiles it in the background so hot entries never
	///     suffer a cold-start compile stall. This effectively favours frequently-accessed lock
	///     expressions over rarely-called ones.</description></item>
	///   <item><description><b>Size 1</b> – each entry counts as one unit against the dedicated
	///     MemoryCache SizeLimit (1 024), capping the total number of cached expressions.</description></item>
	/// </list>
	/// </summary>
	private static readonly FusionCacheEntryOptions CompiledExpressionEntryOptions = new()
	{
		Duration = TimeSpan.FromHours(1),
		EagerRefreshThreshold = 0.9f,
		Size = 1,
	};

	/// <summary>
	/// Returns a compiled delegate for the given lock expression text, using FusionCache to avoid
	/// repeated ANTLR lex-parse-visit work on the hot path.
	/// Entries are tagged so the entire compiled-expression set can be flushed at once.
	/// </summary>
	public Func<AnySharpObject, AnySharpObject, ValueTask<bool>> Compile(string text)
		=> cache.GetOrSet(
			$"{CacheKeyPrefix}{text}",
			_ => CompileInternal(text),
			CompiledExpressionEntryOptions,
			tags: [CompiledExpressionsTag])!;

	/// <summary>
	/// Invalidate a cached compiled expression. Call this when a lock expression changes
	/// (e.g., via @lock or @unlock). If text is null, clears all compiled expressions.
	/// </summary>
	public void InvalidateCache(string? text = null)
	{
		if (text is null)
		{
			cache.RemoveByTag(CompiledExpressionsTag);
			return;
		}

		cache.Remove($"{CacheKeyPrefix}{text}");
	}

	/// <summary>
	/// Builds a lock recognizer with ANTLR's default <c>ConsoleErrorListener</c> replaced by a
	/// collecting one on the parser, and removed outright from the lexer.
	/// <para>
	/// Unlike softcode, lock expressions can genuinely fail to parse — a bare <c>^</c> matches no
	/// lexer rule, and operators with nothing to join are a syntax error — so leaving the default
	/// listeners attached wrote player-triggered diagnostics to the server's stdout, where nobody
	/// sees them.
	/// </para>
	/// </summary>
	private static (SharpMUSHBoolExpParser Parser, ParserErrorListener Errors) CreateParser(string text, string origin)
	{
		var errors = new ParserErrorListener(text);

		StringSpanInputStream inputStream = new(text, origin);
		SharpMUSHBoolExpLexer lexer = new(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
		lexer.RemoveErrorListeners();
		// The lock lexer is not total (a stray '^' matches no rule), so collect its token-recognition
		// errors too — otherwise a trailing bad character is dropped silently and the truncated stream
		// can still parse (e.g. "#TRUE^" -> "#TRUE"), so Validate/Compile would not fail closed.
		lexer.AddErrorListener(errors);

		BufferedTokenSpanStream tokenStream = new(lexer);
		SharpMUSHBoolExpParser parser = new(tokenStream);
		parser.RemoveErrorListeners();
		parser.AddErrorListener(errors);

		return (parser, errors);
	}

	private Func<AnySharpObject, AnySharpObject, ValueTask<bool>> CompileInternal(string text)
	{
		var (sharpParser, errors) = CreateParser(text, nameof(Compile));
		var chatContext = sharpParser.@lock();

		// A malformed lock must never grant access. Validate gates storage at @lock time, but
		// Compile can still be handed text that never passed through it (a lock written before this
		// validation existed, or set through another path), and the visitor below assumes a
		// well-formed tree. Fail closed rather than compiling ANTLR's error-recovery tree into a
		// delegate that silently means something other than what was typed.
		if (errors.HasErrors || !IsSemanticallyValid(chatContext) || !HasBoundOperands(chatContext))
		{
			return static (_, _) => ValueTask.FromResult(false);
		}

		SharpMUSHBooleanExpressionVisitor visitor = new(services, mediator);

		var predicate = visitor.Visit(chatContext);
		// Cached delegates capture only the expression. A budget belongs to each invocation.
		return (gated, unlocker) =>
		{
			var budget = ExecutionBudget.Current;
			if (budget is null) return predicate(gated, unlocker);
			budget.ThrowIfExceeded();
			return EvaluateWithinBudget(predicate(gated, unlocker), budget);
		};
	}

	private static async ValueTask<bool> EvaluateWithinBudget(ValueTask<bool> pending, ExecutionBudget budget)
	{
		var result = await pending;
		budget.ThrowIfExceeded();
		return result;
	}

	/// <summary>
	/// Validate that the expression is valid.
	/// </summary>
	/// <param name="expression"></param>
	/// <param name="lockee">Person to lock.</param>
	/// <returns>Valid or not.</returns>
	public bool Validate(string expression, AnySharpObject lockee)
	{
		var (sharpParser, errors) = CreateParser(expression, nameof(Validate));
		var chatContext = sharpParser.@lock();

		// A malformed expression is not a valid lock. Without this the visitor ran over ANTLR's
		// error-recovery tree and reported whatever survived as valid, so LockService.Set — which
		// gates on this method — stored locks that could never mean what was typed.
		if (errors.HasErrors)
		{
			return false;
		}

		return IsSemanticallyValid(chatContext);
	}

	private static bool IsSemanticallyValid(SharpMUSHBoolExpParser.LockContext context)
		=> new SharpMUSHBooleanExpressionValidationVisitor().Visit(context) == true;

	/// <summary>
	/// Formats lock syntax without looking up object operands.
	/// </summary>
	/// <param name="text">The lock expression to normalize</param>
	/// <returns>The formatted expression, or the original text when invalid.</returns>
	public string Normalize(string text)
	{
		var (sharpParser, errors) = CreateParser(text, nameof(Normalize));
		var chatContext = sharpParser.@lock();

		// Invalid syntax or object identities have no canonical form; preserve the original text.
		if (errors.HasErrors || !IsSemanticallyValid(chatContext))
		{
			return text;
		}

		SharpMUSHBooleanExpressionNormalizationVisitor visitor = new();

		var normalized = visitor.Visit(chatContext);

		return normalized;
	}

	public bool IsBound(string text)
	{
		var (parser, errors) = CreateParser(text, nameof(IsBound));
		var tree = parser.@lock();
		return !errors.HasErrors && IsSemanticallyValid(tree) && HasBoundOperands(tree);
	}

	private static bool HasBoundOperands(IParseTree tree)
		=> ObjectOperands(tree).All(value => DBRef.TryParse(value, out _));

	private static IEnumerable<string> ObjectOperands(IParseTree tree)
	{
		var operand = tree switch
		{
			SharpMUSHBoolExpParser.DefaultExprContext node => node.@string().GetText(),
			SharpMUSHBoolExpParser.OwnerExprContext node => LockLiteralText.ReadOperand(node.objectOperand()),
			SharpMUSHBoolExpParser.CarryExprContext node => LockLiteralText.ReadOperand(node.objectOperand()),
			SharpMUSHBoolExpParser.IndirectExprContext node => LockLiteralText.ReadOperand(node.objectOperand()),
			SharpMUSHBoolExpParser.ExactObjectExprContext node => LockLiteralText.ReadOperand(node.objectOperand()),
			_ => null
		};
		if (operand is not null)
		{
			yield return operand;
			yield break;
		}
		for (var i = 0; i < tree.ChildCount; i++)
			foreach (var child in ObjectOperands(tree.GetChild(i)))
				yield return child;
	}

	private static string UnescapeObjectOperand(string operand)
	{
		if (!operand.Contains('\\')) return operand;
		var decoded = new StringBuilder(operand.Length);
		for (var i = 0; i < operand.Length; i++)
		{
			if (operand[i] == '\\' && i + 1 < operand.Length) i++;
			decoded.Append(operand[i]);
		}
		return decoded.ToString();
	}

	public async ValueTask<Result<string>> BindAsync(string text, AnySharpObject executor, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var (parser, errors) = CreateParser(text, nameof(BindAsync));
		var tree = parser.@lock();
		if (errors.HasErrors || !IsSemanticallyValid(tree)) return new Error<string>("Invalid lock expression.");
		var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var operand in ObjectOperands(tree).Distinct(StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (DBRef.TryParse(operand, out var reference))
			{
				if (await mediator.Send(new GetObjectNodeQuery(reference!.Value), cancellationToken) is not AnySharpObject)
					return new Error<string>($"I don't see {operand} here.");
				bindings[operand] = operand;
			}
			else
			{
				var located = await services.LocateAsync(executor, executor, UnescapeObjectOperand(operand), LocateFlags.All | LocateFlags.ThingsPreference)
					.AsTask().WaitAsync(cancellationToken);
				if (located is not AnySharpObject obj)
					return new Error<string>($"I can't find a unique object matching {operand}.");
				bindings[operand] = $"#{obj.Object().DBRef.Number}";
			}
		}
		var bound = new SharpMUSHBooleanExpressionNormalizationVisitor(value => bindings[value]).Visit(tree);
		return IsBound(bound) ? bound : new Error<string>("Invalid lock expression.");
	}

	public async ValueTask<string> RenderAsync(string text, AnySharpObject viewer, LockRenderMode mode, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var (parser, errors) = CreateParser(text, nameof(RenderAsync));
		var tree = parser.@lock();
		if (errors.HasErrors || !IsSemanticallyValid(tree) || !HasBoundOperands(tree))
			return $"*INVALID LOCK*: {text}";
		var references = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var operand in ObjectOperands(tree).Distinct(StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var reference = DBRef.Parse(operand);
			references[operand] = mode switch
			{
				LockRenderMode.Decompile when viewer.Object().DBRef.Matches(reference) && reference.CreationMilliseconds is null => "me",
				LockRenderMode.Examine => await mediator.Send(new GetObjectNodeQuery(reference), cancellationToken) switch
				{
					AnySharpObject obj => await services.FormatObjectAsync(viewer, obj).AsTask().WaitAsync(cancellationToken),
					_ => "*NOTHING*"
				},
				_ => operand
			};
		}
		var rendered = new SharpMUSHBooleanExpressionNormalizationVisitor(value => references[value], compact: true).Visit(tree);
		if (mode != LockRenderMode.Decompile) return rendered;
		var escaped = new StringBuilder(rendered.Length);
		for (var i = 0; i < rendered.Length; i++)
		{
			var character = rendered[i];
			if (character == ' ' && (i > 0 && rendered[i - 1] == ' ' || i + 1 < rendered.Length && rendered[i + 1] == ' '))
				escaped.Append("%b");
			else if (character is '\r' or '\n')
			{
				escaped.Append("%r");
				if (character == '\r' && i + 1 < rendered.Length && rendered[i + 1] == '\n') i++;
			}
			else if (character == '\t') escaped.Append("%t");
			else
			{
				if ("$%(),;[]\\^{}".Contains(character)) escaped.Append('\\');
				escaped.Append(character);
			}
		}
		return escaped.ToString();
	}

}
