using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calcy.Formulas;

/// <summary>Shape of one category data file (for example <c>Data/mechanics.json</c>).</summary>
internal sealed record FormulaFile
{
    /// <summary>Category id applied to every formula in the file that does not set its own.</summary>
    public string Category { get; init; } = "";

    public List<Formula> Formulas { get; init; } = [];
}

/// <summary>Shape of <c>Data/constants.json</c>.</summary>
internal sealed record ConstantFile
{
    public List<FormulaConstant> Constants { get; init; } = [];
}

/// <summary>Shape of <c>Data/categories.json</c>.</summary>
internal sealed record CategoryFile
{
    public List<FormulaCategory> Categories { get; init; } = [];
}

/// <summary>
/// Source generated serializer metadata, so loading works with trimming and native AOT.
/// Unknown properties are rejected so that typos in the data files fail loudly.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(FormulaFile))]
[JsonSerializable(typeof(ConstantFile))]
[JsonSerializable(typeof(CategoryFile))]
internal sealed partial class FormulaJsonContext : JsonSerializerContext;
