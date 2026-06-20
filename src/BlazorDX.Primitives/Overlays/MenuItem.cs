namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// One entry in a menu. Data-driven so the menu owns roving focus and keyboard
/// activation without each item being a separate component registration.
/// </summary>
/// <param name="Text">The label shown to the user.</param>
/// <param name="OnSelect">Invoked when the item is chosen; the menu then closes.</param>
/// <param name="Disabled">When true the item is skipped by keyboard navigation and not selectable.</param>
public sealed record MenuItem(string Text, Action? OnSelect = null, bool Disabled = false);
