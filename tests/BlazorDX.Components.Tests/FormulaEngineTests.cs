using BlazorDX.Documents.Formula;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Exercises the C# Excel formula engine end-to-end: tokenizer/parser precedence,
/// cell/range resolution, every built-in function, error propagation, dependency
/// recalc ordering, and circular-reference detection.
/// </summary>
public sealed class FormulaEngineTests
{
    // Builds an ICellGrid from a jagged array of raw cell text (literals + formulas)
    // by running the full recalc, so reference-based tests see realistic values.
    private static ICellGrid Computed(params string[][] rows)
    {
        CellValue[][] values = FormulaEngine.Recalculate(rows);
        var grid = new ArrayCellGrid(values.Length, values.Length == 0 ? 0 : values[0].Length);
        for (int r = 0; r < values.Length; r++)
        {
            for (int c = 0; c < values[r].Length; c++)
            {
                grid.SetValue(r, c, values[r][c]);
            }
        }

        return grid;
    }

    private static double Num(CellValue v)
    {
        Assert.Equal(CellValueKind.Number, v.Kind);
        return v.AsRawNumber;
    }

    private static CellValue Eval(string formula) => FormulaEngine.Evaluate(formula);

    // ---- Arithmetic, precedence, parentheses, unary, power -----------------

    [Theory]
    [InlineData("=1+2", 3)]
    [InlineData("=2+3*4", 14)]
    [InlineData("=(2+3)*4", 20)]
    [InlineData("=10/4", 2.5)]
    [InlineData("=2-3-4", -5)]            // left-associative subtraction
    [InlineData("=20/4/5", 1)]            // left-associative division
    [InlineData("=-3+5", 2)]              // unary minus
    [InlineData("=--5", 5)]               // double unary minus
    [InlineData("=+7", 7)]                // unary plus tolerated
    public void Arithmetic_and_precedence(string formula, double expected) =>
        Assert.Equal(expected, Num(Eval(formula)), 10);

    [Theory]
    [InlineData("=2^3", 8)]
    [InlineData("=2^3^2", 512)]           // right-associative: 2^(3^2)
    [InlineData("=-2^2", -4)]             // unary minus looser than power: -(2^2)
    [InlineData("=2^-1", 0.5)]            // negative exponent
    [InlineData("=2*3^2", 18)]            // power binds tighter than *
    public void Power_right_associative_and_precedence(string formula, double expected) =>
        Assert.Equal(expected, Num(Eval(formula)), 10);

    // ---- Cell refs, ranges, concat, comparisons ----------------------------

    [Fact]
    public void Relative_and_absolute_refs_resolve_to_same_value()
    {
        ICellGrid grid = Computed(
            new[] { "10", "20" },
            new[] { "=A1+B1", "=$A$1+$B$1" });

        Assert.Equal(30, Num(FormulaEngine.Evaluate("=A2", grid)));
        Assert.Equal(30, Num(FormulaEngine.Evaluate("=B2", grid)));
    }

    [Fact]
    public void Range_sum_over_two_dimensions()
    {
        ICellGrid grid = Computed(
            new[] { "1", "2" },
            new[] { "3", "4" });

        Assert.Equal(10, Num(FormulaEngine.Evaluate("=SUM(A1:B2)", grid)));
    }

    [Fact]
    public void Concat_operator_joins_text_and_numbers()
    {
        CellValue v = Eval("=\"a\" & 1 & \"b\"");
        Assert.Equal(CellValueKind.Text, v.Kind);
        Assert.Equal("a1b", v.AsRawText);
    }

    [Theory]
    [InlineData("=1=1", true)]
    [InlineData("=1=2", false)]
    [InlineData("=1<>2", true)]
    [InlineData("=2>1", true)]
    [InlineData("=2<1", false)]
    [InlineData("=2>=2", true)]
    [InlineData("=2<=1", false)]
    [InlineData("=\"abc\"=\"ABC\"", true)]   // text compare is case-insensitive
    public void Comparisons_return_booleans(string formula, bool expected)
    {
        CellValue v = Eval(formula);
        Assert.Equal(CellValueKind.Boolean, v.Kind);
        Assert.Equal(expected, v.AsRawBool);
    }

    [Fact]
    public void Empty_cells_treated_as_zero_in_arithmetic()
    {
        ICellGrid grid = Computed(new[] { "", "5" });
        Assert.Equal(5, Num(FormulaEngine.Evaluate("=A1+B1", grid)));
    }

    // ---- Aggregation functions --------------------------------------------

    [Fact]
    public void Sum_average_min_max_count_counta()
    {
        ICellGrid grid = Computed(new[] { "1", "2", "3", "text", "" });

        Assert.Equal(6, Num(FormulaEngine.Evaluate("=SUM(A1:E1)", grid)));
        Assert.Equal(2, Num(FormulaEngine.Evaluate("=AVERAGE(A1:E1)", grid)));
        Assert.Equal(1, Num(FormulaEngine.Evaluate("=MIN(A1:E1)", grid)));
        Assert.Equal(3, Num(FormulaEngine.Evaluate("=MAX(A1:E1)", grid)));
        Assert.Equal(3, Num(FormulaEngine.Evaluate("=COUNT(A1:E1)", grid)));   // numbers only
        Assert.Equal(4, Num(FormulaEngine.Evaluate("=COUNTA(A1:E1)", grid)));  // non-blank
    }

    [Fact]
    public void Average_of_no_numbers_is_div0()
    {
        ICellGrid grid = Computed(new[] { "text", "" });
        Assert.Equal(FormulaError.Div0, FormulaEngine.Evaluate("=AVERAGE(A1:B1)", grid).ErrorValue);
    }

    [Fact]
    public void Sum_mixes_literals_and_ranges()
    {
        ICellGrid grid = Computed(new[] { "1", "2" });
        Assert.Equal(13, Num(FormulaEngine.Evaluate("=SUM(A1:B1, 10)", grid)));
    }

    // ---- Logical functions -------------------------------------------------

    [Theory]
    [InlineData("=IF(1>0, 100, 200)", 100)]
    [InlineData("=IF(1<0, 100, 200)", 200)]
    [InlineData("=IF(TRUE, 1)", 1)]
    public void If_selects_branch(string formula, double expected) =>
        Assert.Equal(expected, Num(Eval(formula)), 10);

    [Fact]
    public void If_omitted_false_branch_returns_false()
    {
        CellValue v = Eval("=IF(1<0, 1)");
        Assert.Equal(CellValueKind.Boolean, v.Kind);
        Assert.False(v.AsRawBool);
    }

    [Theory]
    [InlineData("=AND(TRUE, TRUE)", true)]
    [InlineData("=AND(TRUE, FALSE)", false)]
    [InlineData("=OR(FALSE, FALSE)", false)]
    [InlineData("=OR(FALSE, TRUE)", true)]
    [InlineData("=NOT(FALSE)", true)]
    [InlineData("=NOT(TRUE)", false)]
    [InlineData("=AND(1, 1>0)", true)]
    public void Boolean_logic(string formula, bool expected)
    {
        CellValue v = Eval(formula);
        Assert.Equal(CellValueKind.Boolean, v.Kind);
        Assert.Equal(expected, v.AsRawBool);
    }

    // ---- Math functions ----------------------------------------------------

    [Theory]
    [InlineData("=ABS(-5)", 5)]
    [InlineData("=ABS(5)", 5)]
    [InlineData("=INT(3.9)", 3)]
    [InlineData("=INT(-3.1)", -4)]        // INT floors toward negative infinity
    [InlineData("=SQRT(9)", 3)]
    [InlineData("=ROUND(2.345, 2)", 2.35)]
    [InlineData("=ROUND(2.5, 0)", 3)]     // half away from zero
    [InlineData("=ROUND(-2.5, 0)", -3)]
    [InlineData("=ROUND(123.456, -1)", 120)]
    [InlineData("=MOD(10, 3)", 1)]
    [InlineData("=MOD(-1, 3)", 2)]        // sign follows divisor
    public void Math_functions(string formula, double expected) =>
        Assert.Equal(expected, Num(Eval(formula)), 10);

    [Fact]
    public void Sqrt_of_negative_is_num_error() =>
        Assert.Equal(FormulaError.Num, Eval("=SQRT(-1)").ErrorValue);

    [Fact]
    public void Mod_by_zero_is_div0() =>
        Assert.Equal(FormulaError.Div0, Eval("=MOD(5, 0)").ErrorValue);

    // ---- Text functions ----------------------------------------------------

    [Theory]
    [InlineData("=LEN(\"hello\")", 5)]
    [InlineData("=LEN(\"\")", 0)]
    public void Len_counts_characters(string formula, double expected) =>
        Assert.Equal(expected, Num(Eval(formula)), 10);

    [Theory]
    [InlineData("=LEFT(\"hello\", 2)", "he")]
    [InlineData("=LEFT(\"hi\", 5)", "hi")]       // count beyond length returns all
    [InlineData("=LEFT(\"hello\")", "h")]         // default count 1
    [InlineData("=RIGHT(\"hello\", 3)", "llo")]
    [InlineData("=MID(\"hello\", 2, 3)", "ell")]
    [InlineData("=MID(\"hello\", 4, 10)", "lo")]
    [InlineData("=MID(\"hello\", 9, 2)", "")]     // start beyond length
    [InlineData("=UPPER(\"aBc\")", "ABC")]
    [InlineData("=LOWER(\"aBc\")", "abc")]
    [InlineData("=TRIM(\"  a   b  \")", "a b")]   // collapses internal runs
    [InlineData("=TRIM(\"a\tb\")", "a b")]         // tabs are whitespace: collapse to one space
    [InlineData("=TRIM(\"\ta\t\tb\n\")", "a b")]   // mixed tabs/newlines, trimmed + collapsed
    [InlineData("=CONCAT(\"a\", \"b\", \"c\")", "abc")]
    [InlineData("=CONCATENATE(\"x\", 1, \"y\")", "x1y")]
    public void Text_functions(string formula, string expected)
    {
        CellValue v = Eval(formula);
        Assert.Equal(CellValueKind.Text, v.Kind);
        Assert.Equal(expected, v.AsRawText);
    }

    // ---- Errors & propagation ----------------------------------------------

    [Fact]
    public void Divide_by_zero_is_div0() =>
        Assert.Equal(FormulaError.Div0, Eval("=1/0").ErrorValue);

    [Fact]
    public void Type_mismatch_is_value_error()
    {
        ICellGrid grid = Computed(new[] { "abc" });
        Assert.Equal(FormulaError.Value, FormulaEngine.Evaluate("=A1+1", grid).ErrorValue);
    }

    [Fact]
    public void Unknown_function_is_name_error() =>
        Assert.Equal(FormulaError.Name, Eval("=BOGUS(1)").ErrorValue);

    [Fact]
    public void Unknown_bare_name_is_name_error() =>
        Assert.Equal(FormulaError.Name, Eval("=NOTACELL").ErrorValue);

    [Fact]
    public void Error_propagates_through_arithmetic() =>
        Assert.Equal(FormulaError.Div0, Eval("=1 + (1/0) * 5").ErrorValue);

    [Fact]
    public void Error_propagates_through_function()
    {
        ICellGrid grid = Computed(new[] { "1", "=1/0" });
        Assert.Equal(FormulaError.Div0, FormulaEngine.Evaluate("=SUM(A1:B1)", grid).ErrorValue);
    }

    [Fact]
    public void Comparison_does_not_eat_error() =>
        Assert.Equal(FormulaError.Div0, Eval("=(1/0) > 1").ErrorValue);

    // ---- Dependency recalc -------------------------------------------------

    [Fact]
    public void Recalc_resolves_multilevel_dependencies()
    {
        // C1 = A1+B1, D1 = C1*2; declared out of dependency order on purpose.
        CellValue[][] result = FormulaEngine.Recalculate(new[]
        {
            new[] { "3", "4", "=A1+B1", "=C1*2" },
        });

        Assert.Equal(7, result[0][2].AsRawNumber);    // C1
        Assert.Equal(14, result[0][3].AsRawNumber);   // D1
    }

    [Fact]
    public void Recalc_reflects_changed_input()
    {
        CellValue[][] before = FormulaEngine.Recalculate(new[]
        {
            new[] { "3", "4", "=A1+B1", "=C1*2" },
        });
        Assert.Equal(14, before[0][3].AsRawNumber);

        CellValue[][] after = FormulaEngine.Recalculate(new[]
        {
            new[] { "10", "4", "=A1+B1", "=C1*2" },
        });
        Assert.Equal(14, after[0][2].AsRawNumber);   // C1 = 14
        Assert.Equal(28, after[0][3].AsRawNumber);   // D1 = 28
    }

    [Fact]
    public void Recalc_deep_chain_across_rows()
    {
        // A1=1, A2=A1+1, A3=A2+1, ... topological order across several levels.
        CellValue[][] result = FormulaEngine.Recalculate(new[]
        {
            new[] { "1" },
            new[] { "=A1+1" },
            new[] { "=A2+1" },
            new[] { "=A3+1" },
            new[] { "=A4+1" },
        });

        Assert.Equal(1, result[0][0].AsRawNumber);
        Assert.Equal(2, result[1][0].AsRawNumber);
        Assert.Equal(3, result[2][0].AsRawNumber);
        Assert.Equal(4, result[3][0].AsRawNumber);
        Assert.Equal(5, result[4][0].AsRawNumber);
    }

    // ---- Circular references -----------------------------------------------

    [Fact]
    public void Direct_cycle_yields_circular_error()
    {
        // A1=B1, B1=A1
        CellValue[][] result = FormulaEngine.Recalculate(new[]
        {
            new[] { "=B1", "=A1" },
        });

        Assert.Equal(FormulaError.Circular, result[0][0].ErrorValue);
        Assert.Equal(FormulaError.Circular, result[0][1].ErrorValue);
    }

    [Fact]
    public void Self_reference_yields_circular_error()
    {
        CellValue[][] result = FormulaEngine.Recalculate(new[]
        {
            new[] { "=A1" },
        });

        Assert.Equal(FormulaError.Circular, result[0][0].ErrorValue);
    }

    [Fact]
    public void Indirect_cycle_yields_circular_for_all_members()
    {
        // A1=B1, B1=C1, C1=A1 (three-cell cycle)
        CellValue[][] result = FormulaEngine.Recalculate(new[]
        {
            new[] { "=B1", "=C1", "=A1" },
        });

        Assert.Equal(FormulaError.Circular, result[0][0].ErrorValue);
        Assert.Equal(FormulaError.Circular, result[0][1].ErrorValue);
        Assert.Equal(FormulaError.Circular, result[0][2].ErrorValue);
    }

    [Fact]
    public void Cycle_does_not_poison_independent_cells()
    {
        // A1<->B1 cycle, but C1 = 5+5 is independent and must still compute.
        CellValue[][] result = FormulaEngine.Recalculate(new[]
        {
            new[] { "=B1", "=A1", "=5+5" },
        });

        Assert.Equal(FormulaError.Circular, result[0][0].ErrorValue);
        Assert.Equal(FormulaError.Circular, result[0][1].ErrorValue);
        Assert.Equal(10, result[0][2].AsRawNumber);
    }

    // ---- Column-letter <-> index round-trip --------------------------------

    [Theory]
    [InlineData("A", 0)]
    [InlineData("Z", 25)]
    [InlineData("AA", 26)]
    [InlineData("AB", 27)]
    [InlineData("ZZ", 701)]
    [InlineData("AAA", 702)]
    public void Column_letters_to_index(string letters, int expected) =>
        Assert.Equal(expected, CellAddress.LettersToColumn(letters));

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Column_index_to_letters(int index, string expected) =>
        Assert.Equal(expected, CellAddress.ColumnToLetters(index));

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(701)]
    [InlineData(702)]
    [InlineData(18277)]   // ZZZ + 1 region
    public void Column_roundtrip(int index) =>
        Assert.Equal(index, CellAddress.LettersToColumn(CellAddress.ColumnToLetters(index)));

    // ---- Address parsing ---------------------------------------------------

    [Theory]
    [InlineData("A1", 0, 0, false, false)]
    [InlineData("$A$1", 0, 0, true, true)]
    [InlineData("$B2", 1, 1, true, false)]
    [InlineData("C$10", 9, 2, false, true)]
    [InlineData("aa100", 99, 26, false, false)]
    public void Address_parse(string text, int row, int col, bool colAbs, bool rowAbs)
    {
        Assert.True(CellAddress.TryParse(text, out CellAddress addr));
        Assert.Equal(row, addr.Row);
        Assert.Equal(col, addr.Column);
        Assert.Equal(colAbs, addr.ColumnAbsolute);
        Assert.Equal(rowAbs, addr.RowAbsolute);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A")]
    [InlineData("A0")]
    [InlineData("A")]
    [InlineData("$$A1")]
    public void Address_parse_rejects_malformed(string text) =>
        Assert.False(CellAddress.TryParse(text, out _));

    [Fact]
    public void Address_a1_roundtrip_preserves_dollars()
    {
        Assert.True(CellAddress.TryParse("$C$7", out CellAddress addr));
        Assert.Equal("$C$7", addr.ToA1());
    }

    // ---- Misc / display ----------------------------------------------------

    [Fact]
    public void Boolean_literals_parse()
    {
        Assert.True(Eval("=TRUE").AsRawBool);
        Assert.False(Eval("=FALSE").AsRawBool);
    }

    [Fact]
    public void Formula_without_leading_equals_also_parses() =>
        Assert.Equal(3, Num(Eval("1+2")));

    [Fact]
    public void Error_display_strings()
    {
        Assert.Equal("#DIV/0!", CellValue.Error(FormulaError.Div0).ToDisplayString());
        Assert.Equal("#VALUE!", CellValue.Error(FormulaError.Value).ToDisplayString());
        Assert.Equal("#REF!", CellValue.Error(FormulaError.Ref).ToDisplayString());
        Assert.Equal("#NAME?", CellValue.Error(FormulaError.Name).ToDisplayString());
        Assert.Equal("#N/A", CellValue.Error(FormulaError.Na).ToDisplayString());
        Assert.Equal("#CIRC!", CellValue.Error(FormulaError.Circular).ToDisplayString());
    }

    [Fact]
    public void Recalculate_over_worksheet_model()
    {
        var sheet = new BlazorDX.Documents.Worksheet(
            "Sheet1",
            new IReadOnlyList<string>[]
            {
                new[] { "2", "3", "=A1*B1" },
            },
            3);

        CellValue[][] result = FormulaEngine.Recalculate(sheet);
        Assert.Equal(6, result[0][2].AsRawNumber);
    }

    [Fact]
    public void Literal_error_text_recognised()
    {
        CellValue[][] result = FormulaEngine.Recalculate(new[]
        {
            new[] { "#N/A", "=A1" },
        });
        Assert.Equal(FormulaError.Na, result[0][0].ErrorValue);
        Assert.Equal(FormulaError.Na, result[0][1].ErrorValue);   // propagates
    }
}
