using System.Text.Json;
using System.Text.Json.Serialization;

namespace EpicFightJsonGeneratorApp.Services;

public sealed class EpicFightJsonGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> SaveWeaponCapabilityAsync(
        string outputFolder,
        string itemName,
        int impact,
        int maxStrikes,
        string type,
        bool overwrite)
    {
        Directory.CreateDirectory(outputFolder);

        string outputPath = Path.Combine(outputFolder, $"{itemName}.json");
        if (File.Exists(outputPath) && !overwrite)
        {
            throw new IOException($"File already exists: {outputPath}");
        }

        EpicFightWeaponCapability capability = new(
            new EpicFightAttributes(new EpicFightCommonAttributes(impact, maxStrikes)),
            type);

        await using FileStream stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, capability, SerializerOptions);

        return outputPath;
    }

    private sealed record EpicFightWeaponCapability(
        [property: JsonPropertyName("attributes")] EpicFightAttributes Attributes,
        [property: JsonPropertyName("type")] string Type);

    private sealed record EpicFightAttributes(
        [property: JsonPropertyName("common")] EpicFightCommonAttributes Common);

    private sealed record EpicFightCommonAttributes(
        [property: JsonPropertyName("impact")] int Impact,
        [property: JsonPropertyName("max_strikes")] int MaxStrikes);
}
