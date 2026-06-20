using BlazorDX.Primitives.Grid;

namespace BlazorDX.Demo.Client;

/// <summary>A demo row for <c>DxPivotGrid</c>: City × Tier with a numeric Visits value.</summary>
[GridRow]
public sealed class PivotPerson
{
    [GridColumn("City", Order = 0)]
    public string City { get; set; } = string.Empty;

    [GridColumn("Tier", Order = 1)]
    public string Tier { get; set; } = string.Empty;

    [GridColumn("Visits", Order = 2)]
    public int Visits { get; set; }

    public static IReadOnlyList<PivotPerson> Sample()
    {
        string[] cities = ["Austin", "Berlin", "Cairo", "Denver"];
        string[] tiers = ["Free", "Pro", "Enterprise"];
        Random random = new(20260617);

        List<PivotPerson> people = new(2000);
        for (int i = 0; i < 2000; i++)
        {
            people.Add(new PivotPerson
            {
                City = cities[random.Next(cities.Length)],
                Tier = tiers[random.Next(tiers.Length)],
                Visits = random.Next(1, 100),
            });
        }

        return people;
    }
}
