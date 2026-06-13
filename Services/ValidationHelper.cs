using EpicFightJsonGeneratorApp.Models;

namespace EpicFightJsonGeneratorApp.Services;

public static class ValidationHelper
{
    public static bool TryValidateScanInput(string projectPath, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            errorMessage = "JAR file is not selected.";
            return false;
        }

        if (!IsJarFile(projectPath))
        {
            errorMessage = "Selected JAR file does not exist.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool IsJarFile(string path)
    {
        return File.Exists(path)
            && string.Equals(Path.GetExtension(path), ".jar", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryValidateGenerationInput(
        string projectFolder,
        string outputFolder,
        ItemModelInfo? selectedItem,
        string impactText,
        string maxStrikesText,
        string type,
        out GenerationInput input,
        out string errorMessage)
    {
        input = GenerationInput.Empty;

        if (!TryValidateScanInput(projectFolder, out errorMessage))
        {
            return false;
        }

        if (selectedItem is null)
        {
            errorMessage = "Item is not selected.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            errorMessage = "Output folder is not selected.";
            return false;
        }

        if (!int.TryParse(impactText, out int impact))
        {
            errorMessage = "Impact must be a number.";
            return false;
        }

        if (!int.TryParse(maxStrikesText, out int maxStrikes))
        {
            errorMessage = "Max strikes must be a number.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            errorMessage = "Type cannot be empty.";
            return false;
        }

        input = new GenerationInput(outputFolder, selectedItem.ItemName, impact, maxStrikes, type.Trim());
        errorMessage = string.Empty;
        return true;
    }
}
