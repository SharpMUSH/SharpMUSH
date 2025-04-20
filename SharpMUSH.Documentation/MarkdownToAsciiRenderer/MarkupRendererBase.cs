using System.Runtime.CompilerServices;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public struct MarkupStringContainer
{
	public MString Str;
}

public class MarkupRendererBase(MarkupStringContainer container) : RendererBase
{
	/// <summary>
	/// Renders the specified markdown object (returns the <see cref="MarkupStringContainer"/> as a render object).
	/// </summary>
	/// <param name="markdownObject">The markdown object.</param>
	/// <returns></returns>
	public override object Render(MarkdownObject markdownObject)
	{
		Write(markdownObject);
		return container;
	}
}

/// <summary>
/// Typed <see cref="MarkupRendererBase"/>.
/// </summary>
/// <typeparam name="T">Type of the renderer</typeparam>
/// <seealso cref="RendererBase" />
public abstract class MarkupRendererBase<T> : MarkupRendererBase where T : MarkupRendererBase<T>
{
	private MarkupStringContainer _container;

	private sealed class Indent
	{
		private readonly string? _constant;
		private readonly string[]? _lineSpecific;
		private int _position;

		internal Indent(string constant)
		{
			_constant = constant;
		}

		internal Indent(string[] lineSpecific)
		{
			_lineSpecific = lineSpecific;
		}

		internal string Next()
		{
			if (_constant != null)
			{
				return _constant;
			}

			//if (_lineSpecific.Count == 0) throw new Exception("Indents empty");
			return _position == _lineSpecific!.Length
				? string.Empty
				: _lineSpecific![_position++];
		}
	}

	private bool _previousWasLine;
	private readonly List<Indent> _indents;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextRendererBase{T}"/> class.
	/// </summary>
	/// <param name="writer">The writer.</param>
	protected MarkupRendererBase(MarkupStringContainer writer) : base(writer)
	{
		// We assume that we are starting as if we previously had a newline
		_container = writer;
		_previousWasLine = true;
		_indents = [];
	}

	protected internal void Reset()
	{
		_container.Str = MModule.empty();
		ResetInternal();
	}

	private void ResetInternal()
	{
		typeof(RendererBase).GetField("_childrenDepth")!.SetValue(this, 0);

		_previousWasLine = true;
		_indents.Clear();
	}

	/// <summary>
	/// Ensures a newline.
	/// </summary>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T EnsureLine()
	{
		if (!_previousWasLine)
		{
			_previousWasLine = true;
			_container.Str = MModule.concat(_container.Str, MModule.single(Environment.NewLine));
		}

		return (T)this;
	}

	public void PushIndent(string indent)
	{
		_indents.Add(new Indent(indent));
	}

	public void PushIndent(string[] lineSpecific)
	{
		_indents.Add(new Indent(lineSpecific));

		// ensure that indents are written to the output stream
		// this assumes that calls after PushIndent will write children content
		_previousWasLine = true;
	}

	public void PopIndent()
	{
		if (_indents.Count > 0)
		{
			_indents.RemoveAt(_indents.Count - 1);
		}
		else
		{
			throw new InvalidOperationException("No indent to pop");
		}
	}

	public void ClearIndent() => _indents.Clear();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteIndent()
	{
		if (_previousWasLine)
		{
			WriteIndentCore();
		}
	}

	private void WriteIndentCore()
	{
		_previousWasLine = false;
		for (var i = 0; i < _indents.Count; i++)
		{
			var indent = _indents[i];
			var indentText = indent.Next();
			_container.Str = MModule.concat(_container.Str, MModule.single(indentText));
		}
	}

	/// <summary>
	/// Writes the specified content.
	/// </summary>
	/// <param name="content">The content.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Write(string? content)
	{
		WriteIndent();
		_container.Str = MModule.concat(_container.Str, MModule.single(content));
		return (T)this;
	}

	/// <summary>
	/// Writes the specified char repeated a specified number of times.
	/// </summary>
	/// <param name="c">The char to write.</param>
	/// <param name="count">The number of times to write the char.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal T Write(char c, int count)
	{
		WriteIndent();

		for (var i = 0; i < count; i++)
		{
			_container.Str = MModule.concat(_container.Str, MModule.single(c.ToString()));
		}

		return (T)this;
	}

	/// <summary>
	/// Writes the specified slice.
	/// </summary>
	/// <param name="slice">The slice.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Write(ref StringSlice slice)
	{
		Write(slice.AsSpan());
		return (T)this;
	}

	/// <summary>
	/// Writes the specified slice.
	/// </summary>
	/// <param name="slice">The slice.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Write(StringSlice slice)
	{
		Write(slice.AsSpan());
		return (T)this;
	}

	/// <summary>
	/// Writes the specified character.
	/// </summary>
	/// <param name="content">The content.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Write(char content)
	{
		WriteIndent();
		if (content == '\n')
		{
			_previousWasLine = true;
		}

		_container.Str = MModule.concat(_container.Str, MModule.single(content.ToString()));

		return (T)this;
	}

	/// <summary>
	/// Writes the specified content.
	/// </summary>
	/// <param name="content">The content.</param>
	/// <param name="offset">The offset.</param>
	/// <param name="length">The length.</param>
	/// <returns>This instance</returns>
	public T Write(string content, int offset, int length)
	{
		_container.Str = MModule.concat(_container.Str, MModule.single(content.AsSpan(offset, length).ToString()));

		return (T)this;
	}

	/// <summary>
	/// Writes the specified content.
	/// </summary>
	/// <param name="content">The content.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Write(ReadOnlySpan<char> content)
	{
		if (content.IsEmpty)
		{
			return;
		}

		WriteIndent();
		WriteRaw(content);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void WriteRaw(char content) 
		=> _container.Str = MModule.concat(_container.Str, MModule.single(content.ToString()));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void WriteRaw(string? content) 
		=> _container.Str = MModule.concat(_container.Str, MModule.single(content));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void WriteRaw(ReadOnlySpan<char> content) 
		=> _container.Str = MModule.concat(_container.Str, MModule.single(content.ToString()));

	/// <summary>
	/// Writes a newline.
	/// </summary>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T WriteLine()
	{
		WriteIndent();
		MModule.concat(_container.Str, MModule.single(Environment.NewLine));
		_previousWasLine = true;
		return (T)this;
	}

	/// <summary>
	/// Writes a newline.
	/// </summary>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T WriteLine(NewLine newLine)
	{
		WriteIndent();
		MModule.concat(_container.Str, MModule.single(newLine.ToString()));
		_previousWasLine = true;
		return (T)this;
	}

	/// <summary>
	/// Writes a content followed by a newline.
	/// </summary>
	/// <param name="content">The content.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T WriteLine(string content)
	{
		WriteIndent();
		_previousWasLine = true;
		MModule.concat(_container.Str, MModule.single(content + Environment.NewLine));
		return (T)this;
	}

	/// <summary>
	/// Writes a content followed by a newline.
	/// </summary>
	/// <param name="content">The content.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T WriteLine(char content)
	{
		WriteIndent();
		_previousWasLine = true;
		MModule.concat(_container.Str, MModule.single(content + Environment.NewLine));
		return (T)this;
	}

	/// <summary>
	/// Writes the inlines of a leaf inline.
	/// </summary>
	/// <param name="leafBlock">The leaf block.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T WriteLeafInline(LeafBlock leafBlock)
	{
		Inline? inline = leafBlock.Inline;

		while (inline != null)
		{
			Write(inline);
			inline = inline.NextSibling;
		}

		return (T)this;
	}
}