namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// One option in a Select / Listbox / ComboBox. Data-driven so the component owns
/// roving focus and selection without each option being a separate registration.
/// </summary>
/// <typeparam name="TValue">The option's underlying value type.</typeparam>
/// <param name="Value">The value bound when this option is chosen.</param>
/// <param name="Text">The label shown to the user.</param>
/// <param name="Disabled">When true the option is skipped by navigation and not selectable.</param>
public sealed record ListOption<TValue>(TValue Value, string Text, bool Disabled = false);
