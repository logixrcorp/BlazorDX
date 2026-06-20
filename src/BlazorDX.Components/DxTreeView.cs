using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>A node in a <see cref="DxTreeView"/>. A class so expand/select track by reference.</summary>
public sealed class TreeNode
{
    public TreeNode(string text, IReadOnlyList<TreeNode>? children = null)
    {
        Text = text;
        Children = children ?? [];
    }

    public string Text { get; set; }

    public IReadOnlyList<TreeNode> Children { get; set; }

    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// A hierarchical tree view with expand/collapse, single selection, and keyboard
/// navigation following the WAI-ARIA tree pattern: one tab stop, a roving active node
/// tracked with <c>aria-activedescendant</c>, Arrow keys to move/expand/collapse, and
/// Enter/Space to select. Styling is CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxTreeView : ComponentBase
{
    private readonly HashSet<TreeNode> expanded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TreeNode, ElementReference> elements = new(ReferenceEqualityComparer.Instance);
    private TreeNode? active;
    private TreeNode? focusPending;

    /// <summary>Root nodes.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<TreeNode> Items { get; set; } = [];

    /// <summary>The selected node (two-way bindable via <c>@bind-Selected</c>).</summary>
    [Parameter] public TreeNode? Selected { get; set; }

    /// <summary>Raised when the selection changes.</summary>
    [Parameter] public EventCallback<TreeNode> SelectedChanged { get; set; }

    /// <summary>Expand every node initially.</summary>
    [Parameter] public bool InitiallyExpanded { get; set; }

    /// <summary>Extra CSS classes appended to the root.</summary>
    [Parameter] public string? Class { get; set; }

    private string TreeId { get; } = $"dx-tree-{Guid.NewGuid():N}";

    protected override void OnParametersSet()
    {
        if (InitiallyExpanded && expanded.Count == 0)
        {
            foreach (TreeNode root in Items)
            {
                ExpandAll(root);
            }
        }

        active ??= Items.Count > 0 ? Items[0] : null;
    }

    private void ExpandAll(TreeNode node)
    {
        if (node.HasChildren)
        {
            expanded.Add(node);
            foreach (TreeNode child in node.Children)
            {
                ExpandAll(child);
            }
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        elements.Clear();
        builder.OpenElement(0, "ul");
        builder.AddAttribute(1, "class", $"dx-tree {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "tree");
        builder.AddAttribute(3, "tabindex", "0");
        if (active is not null)
        {
            builder.AddAttribute(4, "aria-activedescendant", NodeId(active));
        }

        builder.AddAttribute(5, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));

        foreach (TreeNode node in Items)
        {
            BuildNode(builder, node, 1);
        }

        builder.CloseElement();
    }

    private void BuildNode(RenderTreeBuilder builder, TreeNode node, int level)
    {
        bool isExpanded = expanded.Contains(node);
        bool isActive = ReferenceEquals(node, active);
        bool isSelected = ReferenceEquals(node, Selected);
        TreeNode captured = node;

        builder.OpenElement(6, "li");
        builder.SetKey(node);
        builder.AddAttribute(7, "role", "treeitem");
        builder.AddAttribute(8, "aria-level", level);
        builder.AddAttribute(9, "aria-selected", isSelected ? "true" : "false");
        if (node.HasChildren)
        {
            builder.AddAttribute(10, "aria-expanded", isExpanded ? "true" : "false");
        }

        // The row: twisty + label, indented by level.
        builder.OpenElement(11, "div");
        builder.AddAttribute(12, "id", NodeId(node));
        builder.AddAttribute(13, "class",
            "dx-tree-row" + (isSelected ? " dx-tree-selected" : string.Empty) + (isActive ? " dx-tree-active" : string.Empty));
        builder.AddAttribute(14, "style", $"padding-left:{level * 16}px");
        builder.AddAttribute(15, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
        builder.AddElementReferenceCapture(16, element => elements[captured] = element);

        if (node.HasChildren)
        {
            builder.OpenElement(17, "button");
            builder.AddAttribute(18, "type", "button");
            builder.AddAttribute(19, "class", "dx-tree-twisty");
            builder.AddAttribute(20, "tabindex", "-1");
            builder.AddAttribute(21, "aria-label", isExpanded ? "Collapse" : "Expand");
            builder.AddAttribute(22, "onclick", EventCallback.Factory.Create(this, () => Toggle(captured)));
            builder.AddEventStopPropagationAttribute(23, "onclick", true);
            builder.AddContent(24, isExpanded ? "▾" : "▸");
            builder.CloseElement();
        }
        else
        {
            builder.OpenElement(25, "span");
            builder.AddAttribute(26, "class", "dx-tree-spacer");
            builder.CloseElement();
        }

        builder.OpenElement(27, "span");
        builder.AddAttribute(28, "class", "dx-tree-label");
        builder.AddContent(29, node.Text);
        builder.CloseElement();

        builder.CloseElement();   // .dx-tree-row

        if (node.HasChildren && isExpanded)
        {
            builder.OpenElement(30, "ul");
            builder.AddAttribute(31, "role", "group");
            builder.AddAttribute(32, "class", "dx-tree-group");
            foreach (TreeNode child in node.Children)
            {
                BuildNode(builder, child, level + 1);
            }

            builder.CloseElement();
        }

        builder.CloseElement();   // li
    }

    private void Toggle(TreeNode node)
    {
        if (!expanded.Remove(node))
        {
            expanded.Add(node);
        }

        active = node;
        StateHasChanged();
    }

    private async Task SelectAsync(TreeNode node)
    {
        active = node;
        if (!ReferenceEquals(node, Selected) && SelectedChanged.HasDelegate)
        {
            await SelectedChanged.InvokeAsync(node);
        }

        StateHasChanged();
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        if (active is null)
        {
            return;
        }

        List<TreeNode> visible = FlattenVisible();
        int index = visible.IndexOf(active);

        switch (args.Key)
        {
            case "ArrowDown":
                if (index < visible.Count - 1)
                {
                    MoveTo(visible[index + 1]);
                }

                break;
            case "ArrowUp":
                if (index > 0)
                {
                    MoveTo(visible[index - 1]);
                }

                break;
            case "ArrowRight":
                if (active.HasChildren && !expanded.Contains(active))
                {
                    Toggle(active);
                }
                else if (active.HasChildren)
                {
                    MoveTo(active.Children[0]);
                }

                break;
            case "ArrowLeft":
                if (active.HasChildren && expanded.Contains(active))
                {
                    Toggle(active);
                }

                break;
            case "Enter" or " ":
                await SelectAsync(active);
                break;
            default:
                return;
        }
    }

    private void MoveTo(TreeNode node)
    {
        active = node;
        focusPending = node;
        StateHasChanged();
    }

    private List<TreeNode> FlattenVisible()
    {
        List<TreeNode> result = new();
        void Walk(IReadOnlyList<TreeNode> nodes)
        {
            foreach (TreeNode node in nodes)
            {
                result.Add(node);
                if (node.HasChildren && expanded.Contains(node))
                {
                    Walk(node.Children);
                }
            }
        }

        Walk(Items);
        return result;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusPending is not null && elements.TryGetValue(focusPending, out ElementReference reference))
        {
            focusPending = null;
            try
            {
                await reference.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Node not in the DOM (collapsed away); ignore.
            }
        }
    }

    private string NodeId(TreeNode node) => $"{TreeId}-{node.GetHashCode():x}";
}
