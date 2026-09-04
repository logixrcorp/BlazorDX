namespace BlazorDX.Primitives.Forms;

/// <summary>
/// Evaluates a conditional field's <c>DependsOn</c> condition against a live model
/// instance. The single evaluator every consumer calls — <c>DxForm</c>'s renderer,
/// <c>DxFormField</c>, the generated <c>Validate</c>, and
/// <see cref="FormTool.ApplyArguments{TModel}"/> — so there is exactly one
/// implementation of "is this field currently active," not one per consumer.
/// </summary>
public static class FormFieldActivity
{
    /// <summary>Convenience overload for runtime callers holding a <see cref="FormFieldInfo"/>.</summary>
    public static bool IsActive<TModel>(IFormModel<TModel> model, TModel instance, FormFieldInfo field) =>
        IsActive(model, instance, field.DependsOn, field.DependsOnValue, field.DependsOnOperator);

    /// <summary>
    /// True when <paramref name="dependsOn"/> is null (unconditional), or when the
    /// named field's current value — read via <see cref="IFormModel{TModel}.GetString"/>,
    /// the same invariant-string form every other consumer already uses — satisfies
    /// <paramref name="dependsOnOperator"/> against <paramref name="dependsOnValue"/>.
    /// Takes raw scalars (not a <see cref="FormFieldInfo"/>) so generated code can call
    /// it directly with compile-time literals, without needing to look up a
    /// <see cref="FormFieldInfo"/> instance by index at runtime.
    /// </summary>
    public static bool IsActive<TModel>(
        IFormModel<TModel> model,
        TModel instance,
        string? dependsOn,
        string? dependsOnValue,
        FormFieldDependsOnOperator dependsOnOperator = FormFieldDependsOnOperator.Equals)
    {
        if (dependsOn is null)
        {
            return true;
        }

        string current = model.GetString(instance, dependsOn);
        return dependsOnOperator switch
        {
            FormFieldDependsOnOperator.NotEmpty => !string.IsNullOrWhiteSpace(current),
            FormFieldDependsOnOperator.NotEquals =>
                !string.Equals(current, dependsOnValue, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(current, dependsOnValue, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Untyped overload for infrastructure code that only holds
    /// <see cref="IFormModelUntyped"/>/<c>object</c> — <c>FormTool</c>'s recursive
    /// schema builder and argument application, which recurse across nested/array
    /// descriptors without knowing each one's TModel. Same evaluator, same rules.
    /// </summary>
    public static bool IsActive(IFormModelUntyped model, object instance, FormFieldInfo field)
    {
        if (field.DependsOn is null)
        {
            return true;
        }

        string current = model.GetString(instance, field.DependsOn);
        return field.DependsOnOperator switch
        {
            FormFieldDependsOnOperator.NotEmpty => !string.IsNullOrWhiteSpace(current),
            FormFieldDependsOnOperator.NotEquals =>
                !string.Equals(current, field.DependsOnValue, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(current, field.DependsOnValue, StringComparison.OrdinalIgnoreCase),
        };
    }
}
