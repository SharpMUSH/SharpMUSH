using System.Runtime.CompilerServices;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public struct MarkupStringContainer
{
	public MString Str;
	public bool Inline;
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
	protected MarkupStringContainer Container;

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
	/// <param name="container">Container for a Markup String.</param>
	protected MarkupRendererBase(MarkupStringContainer container) : base(container)
	{
		// We assume that we are starting as if we previously had a newline
		Container = container;
		_previousWasLine = true;
		_indents = [];
	}

	protected internal void Reset()
	{
		Container.Str = MModule.empty();
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
			WriteToContainer(MModule.single(Environment.NewLine));
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
			WriteToContainer(MModule.single(indentText));
		}
	}

	/// <summary>
	/// Writes the specified content.
	/// </summary>
	/// <param name="content">The content.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Write(MString content)
	{
		WriteIndent();
		WriteToContainer(content);
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
	internal void WriteRaw(ReadOnlySpan<char> content)
		=> WriteToContainer(MModule.single(content.ToString()));
	
	/// <summary>
	/// Writes a newline.
	/// </summary>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T WriteLine()
	{
		WriteIndent();
		WriteToContainer(MModule.single(Environment.NewLine));

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
		
		WriteToContainer(MModule.single(content + Environment.NewLine));

		return (T)this;
	}
	
	/// <summary>
	/// Writes a content followed by a newline.
	/// </summary>
	/// <param name="content">The content.</param>
	/// <returns>This instance</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T WriteLine(MString content)
	{
		WriteIndent();
		_previousWasLine = true;
		
		WriteToContainer(MModule.concat(content,MModule.single(Environment.NewLine)));

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

		Container.Inline = true;
		while (inline != null)
		{
			Write(inline);
			inline = inline.NextSibling;
		}
		Container.Inline = false;

		return (T)this;
	}

	public T WriteToContainer(MString content)
	{
		Container.Str = Container.Inline
			? MModule.insertAt(Container.Str, content, Container.Str.Length)
			: MModule.concat(Container.Str, content);

		return (T)this;
	}
}