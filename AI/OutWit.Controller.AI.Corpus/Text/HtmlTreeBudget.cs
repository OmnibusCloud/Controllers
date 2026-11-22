using AngleSharp.Dom;

namespace OutWit.Controller.AI.Corpus.Text;

/// <summary>
/// The budget the HTML parser is held to while it builds one page's tree, kept on the parser's
/// own tree so that no reading of the markup other than the parser's can argue with it:
/// <list type="bullet">
/// <item><b>Depth.</b> No element may sit at the nesting limit. The parser attaches what would
/// go deeper as siblings at the limit while its stack of open elements keeps growing, and its
/// work per tag grows with that stack: a page that reaches the limit is one whose cost is no
/// longer linear.</item>
/// <item><b>Elements.</b> The tree may not hold more elements than a page may have. The parser
/// tells of every element it makes for a tag (and of the html, head, body and table parts it
/// supplies) but not of the copies it makes on its own: an unclosed <c>&lt;b&gt;</c> is opened
/// again in every block that follows, and four hundred unclosed ones (told apart by an
/// attribute) with five thousand paragraphs after them are two million elements from 23 KB of
/// markup. So the elements nobody told of are found where the parser puts them — around the
/// element made last: among its ancestors, as the last children of an ancestor, or just before
/// one (where a table sends what does not belong in it) — and counted with the others.</item>
/// </list>
/// Checked at every element the parser makes, for the one made before (which is in the tree by
/// then): a walk up its ancestors, at most the nesting limit long. Both limits are functions of
/// the page and the parser version; a page past one has no text (<c>html_too_complex</c>).
/// </summary>
internal sealed class HtmlTreeBudget
{
    #region Constants

    private const string REASON_NESTING = "an element at the nesting limit";
    private const string REASON_ELEMENTS = "more elements than a page may have";

    #endregion

    #region Fields

    private readonly HashSet<IElement> m_counted = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<IElement> m_pending = new();
    private readonly int m_maxDepth;
    private readonly int m_maxElements;
    private IElement? m_last;

    #endregion

    #region Constructors

    /// <summary>Creates a budget.</summary>
    /// <param name="maxDepth">The nesting limit: no element may sit at this depth (the root element is at depth 1).</param>
    /// <param name="maxElements">The most elements the tree may hold.</param>
    public HtmlTreeBudget(int maxDepth, int maxElements)
    {
        m_maxDepth = maxDepth;
        m_maxElements = maxElements;
    }

    #endregion

    #region Functions

    /// <summary>Starts a page.</summary>
    public void Reset()
    {
        m_counted.Clear();
        m_pending.Clear();
        m_last = null;
    }

    /// <summary>The parser made an element (not yet in the tree): the one made before is judged now.</summary>
    /// <param name="element">The new element.</param>
    /// <exception cref="HtmlTooComplexException">The tree is past its budget.</exception>
    public void Created(IElement element)
    {
        Settle();
        m_last = element;
    }

    /// <summary>The parse is over: the element made last is judged.</summary>
    /// <exception cref="HtmlTooComplexException">The tree is past its budget.</exception>
    public void Finish()
    {
        Settle();
        m_last = null;
    }

    /// <summary>
    /// Counts the element made last and whatever the parser put around it without telling: every
    /// ancestor not counted yet, and for the element and each ancestor the uncounted subtrees
    /// that end its children and that stand just before it.
    /// </summary>
    private void Settle()
    {
        var depth = 0;
        for (var current = m_last; current != null; current = current.ParentElement)
        {
            if (++depth >= m_maxDepth)
                throw new HtmlTooComplexException(REASON_NESTING);

            Count(current);

            // What ends its children.
            var children = current.ChildNodes;
            CountUncountedRun(children, children.Length - 1);

            // What stands just before it. The tree grows at its right edge, so the element is
            // found from the end of its parent's children, never by a search from the start.
            if (current.Parent is { } parent)
            {
                var siblings = parent.ChildNodes;
                var index = siblings.Length - 1;
                while (index >= 0 && !ReferenceEquals(siblings[index], current))
                    index--;

                CountUncountedRun(siblings, index - 1);
            }
        }
    }

    /// <summary>
    /// Counts the uncounted element subtrees that end a list of nodes at a position: from there
    /// backwards, over what is no element, to the first element that is counted already.
    /// </summary>
    private void CountUncountedRun(INodeList nodes, int from)
    {
        for (var index = from; index >= 0; index--)
        {
            if (nodes[index] is not IElement root)
                continue;

            if (m_counted.Contains(root))
                return;

            m_pending.Push(root);
            while (m_pending.Count > 0)
            {
                var node = m_pending.Pop();
                if (!Count(node))
                    continue;

                foreach (var child in node.ChildNodes)
                {
                    if (child is IElement element)
                        m_pending.Push(element);
                }
            }
        }
    }

    /// <summary>Counts an element once; false when it was counted before.</summary>
    private bool Count(IElement element)
    {
        if (!m_counted.Add(element))
            return false;

        if (m_counted.Count > m_maxElements)
            throw new HtmlTooComplexException(REASON_ELEMENTS);

        return true;
    }

    #endregion

    #region Properties

    /// <summary>The elements counted so far for the page.</summary>
    public int Elements => m_counted.Count;

    #endregion
}
