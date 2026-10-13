namespace Calcy.Units;

/// <summary>A group of units shown together (Length, Temperature, Data, ...). Most categories hold a single dimension.</summary>
public sealed class UnitCategory
{
    internal UnitCategory(string id, string name, IReadOnlyList<Unit> units, Unit defaultFrom, Unit defaultTo)
    {
        Id = id;
        Name = name;
        Units = units;
        DefaultFrom = defaultFrom;
        DefaultTo = defaultTo;
        Dimensions = [.. units.Select(u => u.Dimension).Distinct()];
    }

    /// <summary>Stable lower-case identifier, for example "length" or "fuel-economy".</summary>
    public string Id { get; }

    /// <summary>Display name, for example "Fuel economy".</summary>
    public string Name { get; }

    /// <summary>The units of the category in display order.</summary>
    public IReadOnlyList<Unit> Units { get; }

    /// <summary>The unit converted from when the category is opened.</summary>
    public Unit DefaultFrom { get; }

    /// <summary>The unit converted to when the category is opened.</summary>
    public Unit DefaultTo { get; }

    /// <summary>The dimensions present (one, except for Electric, which groups charge, current, voltage and resistance).</summary>
    public IReadOnlyList<Dimension> Dimensions { get; }

    /// <summary>True when the unit is listed in this category.</summary>
    public bool Contains(Unit unit) => Units.Contains(unit);

    /// <summary>The listed units that convert to and from <paramref name="unit"/>.</summary>
    public IReadOnlyList<Unit> CompatibleWith(Unit unit) => [.. Units.Where(u => u.Dimension == unit.Dimension)];

    /// <inheritdoc />
    public override string ToString() => Name;
}
