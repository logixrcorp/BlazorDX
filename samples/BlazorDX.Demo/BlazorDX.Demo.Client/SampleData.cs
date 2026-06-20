namespace BlazorDX.Demo.Client;

/// <summary>Generates a deterministic set of demo rows.</summary>
public static class SampleData
{
    private static readonly string[] FirstNames =
        ["Avery", "Bri", "Cy", "Devon", "Esan", "Fen", "Gale", "Hana", "Ira", "Jo"];

    private static readonly string[] Cities =
        ["Austin", "Berlin", "Cairo", "Denver", "Edinburgh", "Faro", "Geneva", "Hanoi"];

    public static IReadOnlyList<PersonRow> People(int count)
    {
        // Fixed seed: the same data every run, so the demo is reproducible.
        Random random = new(20260616);
        List<PersonRow> people = new(count);
        for (int i = 0; i < count; i++)
        {
            people.Add(new PersonRow
            {
                Id = i + 1,
                Name = $"{FirstNames[random.Next(FirstNames.Length)]} #{i + 1}",
                City = Cities[random.Next(Cities.Length)],
                Score = Math.Round(random.NextDouble() * 100, 2),
                Visits = random.Next(0, 500),
            });
        }

        return people;
    }
}
