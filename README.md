# Epic Fight JSON Editor

Small Windows tool for Minecraft mod creators. It scans item model JSON files, shows handheld weapons, previews the GUI texture, and generates Epic Fight weapon capability JSON files.

## Download and run

1. Open the `dist` folder.
2. Download `EpicFightJsonGeneratorApp.exe`.
3. Run the exe.

## Input sources

The app can scan either:

- a Minecraft mod project folder
- a `src/main/resources` folder
- a compiled Minecraft mod `.jar` file

Use:

- `Browse Folder...` for a project/resources folder
- `Browse JAR...` for a mod jar
- `Scan` to load items

## Output folder

Use `Output Folder` and `Browse...` to choose where the final generated JSON file should be saved.

If you do not choose manually, the app suggests:

```text
data/<modid>/capabilities/weapons
```

## Generated JSON example

```json
{
  "attributes": {
    "common": {
      "impact": 2,
      "max_strikes": 2
    }
  },
  "type": "epicfight:axe"
}
```

## Supported item model format

The app detects item models with root parent:

```json
{
  "parent": "minecraft:item/handheld",
  "loader": "neoforge:separate_transforms",
  "base": {
    "parent": "examplemod:item/example_weapon_3d"
  },
  "perspectives": {
    "gui": {
      "parent": "examplemod:item/example_weapon_gui"
    }
  }
}
```

The old parent format is also supported:

```json
{
  "parent": "item/handheld",
  "textures": {
    "layer0": "examplemod:item/example_weapon"
  }
}
```

GUI and 3D variants are not shown as main items.

## Development

```powershell
dotnet run
```

