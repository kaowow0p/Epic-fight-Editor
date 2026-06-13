using System.IO.Compression;
using System.Text.Json;
using EpicFightJsonGeneratorApp.Models;

namespace EpicFightJsonGeneratorApp.Services;

public sealed class ItemModelScanner
{
    private const string PrimaryHandheldParent = "minecraft:item/handheld";
    private const string LegacyHandheldParent = "item/handheld";

    public async Task<IReadOnlyList<ItemModelInfo>> ScanHandheldItemsAsync(string selectedProjectPath)
    {
        if (IsJarFile(selectedProjectPath))
        {
            return await ScanJarAsync(selectedProjectPath);
        }

        return await ScanResourcesFolderAsync(selectedProjectPath);
    }

    public string ResolveResourcesFolder(string selectedProjectPath)
    {
        if (string.IsNullOrWhiteSpace(selectedProjectPath))
        {
            throw new ArgumentException("Project folder or JAR file is not selected.", nameof(selectedProjectPath));
        }

        if (Directory.Exists(Path.Combine(selectedProjectPath, "assets")))
        {
            return selectedProjectPath;
        }

        string nestedResourcesFolder = Path.Combine(selectedProjectPath, "src", "main", "resources");
        if (Directory.Exists(nestedResourcesFolder))
        {
            return nestedResourcesFolder;
        }

        throw new DirectoryNotFoundException(
            $"Resources folder was not found. Select the mod root folder, src/main/resources folder, or a .jar file.");
    }

    private async Task<IReadOnlyList<ItemModelInfo>> ScanResourcesFolderAsync(string selectedProjectPath)
    {
        string resourcesFolder = ResolveResourcesFolder(selectedProjectPath);
        string assetsFolder = Path.Combine(resourcesFolder, "assets");

        if (!Directory.Exists(assetsFolder))
        {
            throw new DirectoryNotFoundException($"Assets folder was not found: {assetsFolder}");
        }

        List<ItemModelInfo> items = new();
        foreach (string modFolder in Directory.EnumerateDirectories(assetsFolder))
        {
            string modId = Path.GetFileName(modFolder);
            string itemModelsFolder = Path.Combine(modFolder, "models", "item");

            if (!Directory.Exists(itemModelsFolder))
            {
                continue;
            }

            IReadOnlyList<ItemModelInfo> modItems = await ScanItemModelsFolderAsync(
                resourcesFolder,
                itemModelsFolder,
                modId);
            items.AddRange(modItems);
        }

        if (items.Count == 0 && !HasAnyItemModelsFolder(assetsFolder))
        {
            throw new DirectoryNotFoundException(
                $"No item model folder was found under: {Path.Combine(assetsFolder, "<modid>", "models", "item")}");
        }

        return SortItems(items);
    }

    private static async Task<IReadOnlyList<ItemModelInfo>> ScanJarAsync(string jarFilePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(jarFilePath);
        List<ItemModelInfo> items = new();

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!TryParseTopLevelItemModelEntry(entry.FullName, out string modId, out string itemName))
            {
                continue;
            }

            await using Stream stream = entry.Open();
            ItemModelData modelData = await ReadItemModelDataAsync(stream);

            if (!IsHandheldParent(modelData.Parent) || IsVariantModel(itemName))
            {
                continue;
            }

            GuiModelResolution guiModel = await ResolveJarGuiModelAsync(
                archive,
                modId,
                itemName,
                modelData.GuiModelReference);

            items.Add(new ItemModelInfo(
                itemName,
                $"{jarFilePath}::{entry.FullName}",
                modelData.Parent!,
                modId,
                guiModel.ModelReference,
                guiModel.ModelFilePath,
                guiModel.TextureReference,
                null,
                guiModel.TextureBytes));
        }

        if (items.Count == 0 && !HasAnyJarItemModelsFolder(archive))
        {
            throw new DirectoryNotFoundException("No assets/<modid>/models/item folder was found inside the selected JAR file.");
        }

        return SortItems(items);
    }

    private static async Task<IReadOnlyList<ItemModelInfo>> ScanItemModelsFolderAsync(
        string resourcesFolder,
        string itemModelsFolder,
        string modId)
    {
        List<ItemModelInfo> result = new();

        foreach (string jsonFilePath in Directory.EnumerateFiles(itemModelsFolder, "*.json", SearchOption.TopDirectoryOnly))
        {
            string itemName = Path.GetFileNameWithoutExtension(jsonFilePath);
            await using FileStream stream = File.OpenRead(jsonFilePath);
            ItemModelData modelData = await ReadItemModelDataAsync(stream);

            if (!IsHandheldParent(modelData.Parent) || IsVariantModel(itemName))
            {
                continue;
            }

            GuiModelResolution guiModel = await ResolveFileSystemGuiModelAsync(
                resourcesFolder,
                itemModelsFolder,
                modId,
                itemName,
                modelData.GuiModelReference);

            result.Add(new ItemModelInfo(
                itemName,
                jsonFilePath,
                modelData.Parent!,
                modId,
                guiModel.ModelReference,
                guiModel.ModelFilePath,
                guiModel.TextureReference,
                guiModel.TextureFilePath));
        }

        return result;
    }

    private static async Task<GuiModelResolution> ResolveFileSystemGuiModelAsync(
        string resourcesFolder,
        string itemModelsFolder,
        string defaultNamespace,
        string itemName,
        string? guiModelReference)
    {
        string? resolvedModelReference = string.IsNullOrWhiteSpace(guiModelReference)
            ? null
            : guiModelReference.Trim();
        string? modelFilePath = null;
        string modelNamespace = defaultNamespace;

        if (!string.IsNullOrWhiteSpace(resolvedModelReference))
        {
            ResourceLocation modelLocation = ParseResourceLocation(resolvedModelReference, defaultNamespace);
            modelNamespace = modelLocation.Namespace;
            modelFilePath = ResolveModelFilePath(resourcesFolder, modelLocation);
        }
        else
        {
            (resolvedModelReference, modelFilePath) = FindFallbackGuiModel(itemModelsFolder, itemName);
        }

        if (string.IsNullOrWhiteSpace(modelFilePath) || !File.Exists(modelFilePath))
        {
            return new GuiModelResolution(resolvedModelReference, modelFilePath, null, null, null);
        }

        try
        {
            await using FileStream stream = File.OpenRead(modelFilePath);
            ItemModelData guiModelData = await ReadItemModelDataAsync(stream);
            string? textureReference = guiModelData.Layer0TextureReference;
            string? textureFilePath = ResolveTextureFilePath(resourcesFolder, textureReference, modelNamespace);

            return new GuiModelResolution(
                resolvedModelReference,
                modelFilePath,
                textureReference,
                textureFilePath,
                null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new GuiModelResolution(resolvedModelReference, modelFilePath, null, null, null);
        }
    }

    private static async Task<GuiModelResolution> ResolveJarGuiModelAsync(
        ZipArchive archive,
        string defaultNamespace,
        string itemName,
        string? guiModelReference)
    {
        string? resolvedModelReference = string.IsNullOrWhiteSpace(guiModelReference)
            ? null
            : guiModelReference.Trim();
        string? modelEntryPath = null;
        string modelNamespace = defaultNamespace;

        if (!string.IsNullOrWhiteSpace(resolvedModelReference))
        {
            ResourceLocation modelLocation = ParseResourceLocation(resolvedModelReference, defaultNamespace);
            modelNamespace = modelLocation.Namespace;
            modelEntryPath = ResolveJarModelEntryPath(modelLocation);
        }
        else
        {
            (resolvedModelReference, modelEntryPath) = FindFallbackJarGuiModel(archive, defaultNamespace, itemName);
        }

        ZipArchiveEntry? modelEntry = string.IsNullOrWhiteSpace(modelEntryPath)
            ? null
            : archive.GetEntry(modelEntryPath);

        if (modelEntry is null)
        {
            return new GuiModelResolution(resolvedModelReference, modelEntryPath, null, null, null);
        }

        try
        {
            await using Stream stream = modelEntry.Open();
            ItemModelData guiModelData = await ReadItemModelDataAsync(stream);
            string? textureReference = guiModelData.Layer0TextureReference;
            byte[]? textureBytes = ResolveJarTextureBytes(archive, textureReference, modelNamespace);

            return new GuiModelResolution(
                resolvedModelReference,
                modelEntryPath,
                textureReference,
                null,
                textureBytes);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new GuiModelResolution(resolvedModelReference, modelEntryPath, null, null, null);
        }
    }

    private static (string? ModelReference, string? ModelFilePath) FindFallbackGuiModel(
        string itemModelsFolder,
        string itemName)
    {
        foreach (string candidateName in GetGuiModelCandidates(itemName))
        {
            string candidatePath = Path.Combine(itemModelsFolder, $"{candidateName}.json");
            if (File.Exists(candidatePath))
            {
                return (candidateName, candidatePath);
            }
        }

        return (null, null);
    }

    private static (string? ModelReference, string? ModelEntryPath) FindFallbackJarGuiModel(
        ZipArchive archive,
        string modId,
        string itemName)
    {
        foreach (string candidateName in GetGuiModelCandidates(itemName))
        {
            string candidateEntryPath = $"assets/{modId}/models/item/{candidateName}.json";
            if (archive.GetEntry(candidateEntryPath) is not null)
            {
                return (candidateName, candidateEntryPath);
            }
        }

        return (null, null);
    }

    private static IEnumerable<string> GetGuiModelCandidates(string itemName)
    {
        yield return $"{itemName}_gui";
        yield return $"{itemName}gui";
    }

    private static async Task<ItemModelData> ReadItemModelDataAsync(Stream jsonStream)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(jsonStream);
        JsonElement root = document.RootElement;

        string? parent = ReadStringProperty(root, "parent");
        string? guiModelReference = null;

        if (root.TryGetProperty("perspectives", out JsonElement perspectivesElement)
            && perspectivesElement.ValueKind == JsonValueKind.Object
            && perspectivesElement.TryGetProperty("gui", out JsonElement guiElement)
            && guiElement.ValueKind == JsonValueKind.Object)
        {
            guiModelReference = ReadStringProperty(guiElement, "parent");
        }

        string? layer0TextureReference = null;
        if (root.TryGetProperty("textures", out JsonElement texturesElement)
            && texturesElement.ValueKind == JsonValueKind.Object)
        {
            layer0TextureReference = ReadStringProperty(texturesElement, "layer0");
        }

        return new ItemModelData(parent, guiModelReference, layer0TextureReference);
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement propertyElement)
            && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;
    }

    private static bool IsHandheldParent(string? parent)
    {
        return string.Equals(parent, PrimaryHandheldParent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parent, LegacyHandheldParent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVariantModel(string itemName)
    {
        return itemName.EndsWith("_gui", StringComparison.OrdinalIgnoreCase)
            || itemName.EndsWith("gui", StringComparison.OrdinalIgnoreCase)
            || itemName.EndsWith("_3d", StringComparison.OrdinalIgnoreCase)
            || itemName.EndsWith("3d", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveTextureFilePath(
        string resourcesFolder,
        string? textureReference,
        string defaultNamespace)
    {
        if (string.IsNullOrWhiteSpace(textureReference))
        {
            return null;
        }

        ResourceLocation textureLocation = ParseResourceLocation(textureReference, defaultNamespace);
        string texturePath = Path.Combine(
            resourcesFolder,
            "assets",
            textureLocation.Namespace,
            "textures",
            ToPlatformPath(textureLocation.Path) + ".png");

        return File.Exists(texturePath) ? texturePath : null;
    }

    private static byte[]? ResolveJarTextureBytes(
        ZipArchive archive,
        string? textureReference,
        string defaultNamespace)
    {
        if (string.IsNullOrWhiteSpace(textureReference))
        {
            return null;
        }

        ResourceLocation textureLocation = ParseResourceLocation(textureReference, defaultNamespace);
        string textureEntryPath = $"assets/{textureLocation.Namespace}/textures/{textureLocation.Path}.png";
        ZipArchiveEntry? textureEntry = archive.GetEntry(textureEntryPath);
        if (textureEntry is null)
        {
            return null;
        }

        using MemoryStream memoryStream = new();
        using Stream entryStream = textureEntry.Open();
        entryStream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static string ResolveModelFilePath(string resourcesFolder, ResourceLocation modelLocation)
    {
        return Path.Combine(
            resourcesFolder,
            "assets",
            modelLocation.Namespace,
            "models",
            ToPlatformPath(modelLocation.Path) + ".json");
    }

    private static string ResolveJarModelEntryPath(ResourceLocation modelLocation)
    {
        return $"assets/{modelLocation.Namespace}/models/{modelLocation.Path}.json";
    }

    private static ResourceLocation ParseResourceLocation(string reference, string defaultNamespace)
    {
        string trimmedReference = reference.Trim();
        int separatorIndex = trimmedReference.IndexOf(':', StringComparison.Ordinal);

        if (separatorIndex < 0)
        {
            return new ResourceLocation(defaultNamespace, trimmedReference);
        }

        string namespaceName = trimmedReference[..separatorIndex];
        string resourcePath = trimmedReference[(separatorIndex + 1)..];

        return new ResourceLocation(namespaceName, resourcePath);
    }

    private static string ToPlatformPath(string resourcePath)
    {
        return resourcePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    private static bool IsJarFile(string path)
    {
        return File.Exists(path)
            && string.Equals(Path.GetExtension(path), ".jar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseTopLevelItemModelEntry(string entryPath, out string modId, out string itemName)
    {
        modId = string.Empty;
        itemName = string.Empty;

        string[] parts = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5
            || !string.Equals(parts[0], "assets", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "models", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[3], "item", StringComparison.OrdinalIgnoreCase)
            || !parts[4].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        modId = parts[1];
        itemName = Path.GetFileNameWithoutExtension(parts[4]);
        return true;
    }

    private static bool HasAnyItemModelsFolder(string assetsFolder)
    {
        return Directory.EnumerateDirectories(assetsFolder)
            .Any(modFolder => Directory.Exists(Path.Combine(modFolder, "models", "item")));
    }

    private static bool HasAnyJarItemModelsFolder(ZipArchive archive)
    {
        return archive.Entries.Any(entry =>
            entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.Contains("/models/item/", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ItemModelInfo> SortItems(IEnumerable<ItemModelInfo> items)
    {
        return items
            .OrderBy(item => item.ModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record ItemModelData(
        string? Parent,
        string? GuiModelReference,
        string? Layer0TextureReference);

    private sealed record GuiModelResolution(
        string? ModelReference,
        string? ModelFilePath,
        string? TextureReference,
        string? TextureFilePath,
        byte[]? TextureBytes);

    private readonly record struct ResourceLocation(string Namespace, string Path);
}
