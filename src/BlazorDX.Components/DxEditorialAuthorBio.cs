using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A richer byline for a <see cref="DxEditorialLayout"/> piece — avatar, name, role, and a short
/// bio — composing the library's own <see cref="DxAvatar"/> rather than a plain text line.
/// Typically placed once, near the end of a piece (the common "about the author" convention),
/// leaving <see cref="DxEditorialLayout"/>'s own hero byline as the compact top-of-page credit.
/// </summary>
public sealed class DxEditorialAuthorBio : ComponentBase
{
    [Parameter, EditorRequired] public string Name { get; set; } = "";

    [Parameter] public string? Role { get; set; }

    [Parameter] public string? AvatarImageUrl { get; set; }

    /// <summary>Defaults to the first letter of the first and last words of <see cref="Name"/>.</summary>
    [Parameter] public string? Initials { get; set; }

    /// <summary>If set, the name links here (e.g. a profile page or the author's site).</summary>
    [Parameter] public string? ProfileUrl { get; set; }

    /// <summary>The bio text.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private string ResolvedInitials
    {
        get
        {
            if (!string.IsNullOrEmpty(Initials))
            {
                return Initials;
            }

            string[] words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length switch
            {
                0 => "",
                1 => words[0][..1].ToUpperInvariant(),
                _ => (words[0][..1] + words[^1][..1]).ToUpperInvariant(),
            };
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-editorial-author-bio");

        builder.OpenComponent<DxAvatar>(2);
        builder.AddComponentParameter(3, nameof(DxAvatar.ImageUrl), AvatarImageUrl);
        builder.AddComponentParameter(4, nameof(DxAvatar.Initials), ResolvedInitials);
        builder.AddComponentParameter(5, nameof(DxAvatar.AltText), Name);
        builder.AddComponentParameter(6, nameof(DxAvatar.Size), 56);
        builder.AddComponentParameter(7, nameof(DxAvatar.Class), "dx-editorial-author-bio-avatar");
        builder.CloseComponent();

        builder.OpenElement(8, "div");
        builder.AddAttribute(9, "class", "dx-editorial-author-bio-text");

        builder.OpenElement(10, "p");
        builder.AddAttribute(11, "class", "dx-editorial-author-bio-name");
        if (!string.IsNullOrEmpty(ProfileUrl))
        {
            builder.OpenElement(12, "a");
            builder.AddAttribute(13, "href", ProfileUrl);
            builder.AddContent(14, Name);
            builder.CloseElement();
        }
        else
        {
            builder.AddContent(15, Name);
        }
        builder.CloseElement(); // .dx-editorial-author-bio-name

        if (!string.IsNullOrEmpty(Role))
        {
            builder.OpenElement(16, "p");
            builder.AddAttribute(17, "class", "dx-editorial-author-bio-role");
            builder.AddContent(18, Role);
            builder.CloseElement();
        }

        builder.OpenElement(19, "div");
        builder.AddAttribute(20, "class", "dx-editorial-author-bio-body");
        builder.AddContent(21, ChildContent);
        builder.CloseElement();

        builder.CloseElement(); // .dx-editorial-author-bio-text
        builder.CloseElement(); // .dx-editorial-author-bio
    }
}
