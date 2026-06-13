namespace EpicFightJsonGeneratorApp.Models;

public sealed record GenerationInput(
    string OutputFolder,
    string ItemName,
    int Impact,
    int MaxStrikes,
    string Type)
{
    public static GenerationInput Empty { get; } = new(string.Empty, string.Empty, 0, 0, string.Empty);
}
