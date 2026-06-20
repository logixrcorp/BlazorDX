using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace BlazorDX.SourceGen;

/// <summary>One column discovered on a row type.</summary>
/// <param name="Editable">Whether the property has a usable (non-init-only) setter.</param>
/// <param name="IsString">Whether the property type is <c>string</c> (assigned directly on edit).</param>
/// <param name="ParseTypeFqn">Fully-qualified non-nullable type used to parse edited text (numeric columns).</param>
internal sealed record GridColumnModel(
    string Header,
    int Order,
    string PropertyName,
    bool IsNumeric,
    bool Editable,
    bool IsString,
    string ParseTypeFqn);

/// <summary>
/// Everything the emitter needs about a <c>[GridRow]</c> type. Columns are stored
/// pre-sorted by <see cref="GridColumnModel.Order"/>.
/// </summary>
internal sealed record GridRowModel(
    string? Namespace,
    string TypeName,
    string Accessibility,
    ImmutableArray<GridColumnModel> Columns);

/// <summary>Helpers for reading the row/column attributes off a symbol.</summary>
internal static class GridRowAnalysis
{
    private const string ColumnAttribute = "BlazorDX.Primitives.Grid.GridColumnAttribute";

    public static GridRowModel Build(INamedTypeSymbol rowType)
    {
        ImmutableArray<GridColumnModel> columns = ReadColumns(rowType);
        string? containingNamespace = rowType.ContainingNamespace.IsGlobalNamespace
            ? null
            : rowType.ContainingNamespace.ToDisplayString();
        string accessibility = rowType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        return new GridRowModel(containingNamespace, rowType.Name, accessibility, columns);
    }

    private static ImmutableArray<GridColumnModel> ReadColumns(INamedTypeSymbol rowType)
    {
        ImmutableArray<GridColumnModel>.Builder builder = ImmutableArray.CreateBuilder<GridColumnModel>();
        foreach (ISymbol member in rowType.GetMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            AttributeData? columnAttribute = FindColumnAttribute(property);
            if (columnAttribute is null)
            {
                continue;
            }

            string header = columnAttribute.ConstructorArguments.Length > 0
                ? columnAttribute.ConstructorArguments[0].Value?.ToString() ?? property.Name
                : property.Name;
            int order = ReadOrder(columnAttribute);

            bool editable = property.SetMethod is { IsInitOnly: false }
                && property.SetMethod.DeclaredAccessibility == Accessibility.Public;
            bool isString = property.Type.SpecialType == SpecialType.System_String;
            string parseTypeFqn = Underlying(property.Type)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Add(new GridColumnModel(
                header, order, property.Name, IsNumeric(property.Type), editable, isString, parseTypeFqn));
        }

        builder.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        return builder.ToImmutable();
    }

    private static AttributeData? FindColumnAttribute(IPropertySymbol property)
    {
        foreach (AttributeData attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ColumnAttribute)
            {
                return attribute;
            }
        }

        return null;
    }

    private static int ReadOrder(AttributeData columnAttribute)
    {
        foreach (KeyValuePair<string, TypedConstant> named in columnAttribute.NamedArguments)
        {
            if (named.Key == "Order" && named.Value.Value is int order)
            {
                return order;
            }
        }

        return 0;
    }

    // Unwraps Nullable<T> to its underlying type (e.g. int? -> int), else returns the type.
    private static ITypeSymbol Underlying(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static bool IsNumeric(ITypeSymbol type)
    {
        type = Underlying(type);

        return type.SpecialType
            is SpecialType.System_Byte or SpecialType.System_SByte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_Decimal;
    }
}
