# cash-shop-wz-extractor
A tool that utilizes MapleLib to parse relevant wz files &amp; dump data for upcoming &amp; past cash shop updates

This tool uses MapleLib/WzLib to parse through Commodity.img to gather which items we need to gather info on for the current patches sales.

This is the first thing I've written in C# and I got pretty lost in the weeds with all of the classes in WzLib, so it is very messy and bad! Maybe one day I'll re-write it.

## WzDataExtractor

WzDataExtractor is the primary tool in this project for extracting and analyzing data from MapleStory WZ files, focusing on cash shop items and related assets.

### Overview
This tool parses WZ files to identify and extract information about cash shop items, including their properties, icons, and associated data. It generates XML files for item metadata and PNG files for visual assets, making it easier to analyze and process cash shop updates. The tool relies on MapleLib for WZ file handling and is designed to work with standard MapleStory WZ structures.

### Usage
Run the tool from the command line with the following syntax:

```
WzDataExtractor.exe [options]
```

#### Command-Line Options
- `-d <path>` or `--data <path>`: Path to the MapleStory WZ files directory (required).
- `-o <path>` or `--out <path>`: Output directory for extracted data (required).
- `--verbose`: Enable detailed logging for debugging.

#### Examples
1. **Basic Extraction**:
   ```
   WzDataExtractor.exe -d "C:\MapleStory\WZ" -o "C:\Output" --verbose
   ```

#### Output
- **Item Data**: XML files containing item properties, organized by category in `output/CharacterItems/{category}`.
- **Icons and Sprites**: PNG files for item visuals in the same directory structure.
- Additional analysis files may be generated based on the WZ content.

## DamageSkinExtractor

DamageSkinExtractor is a specialized tool within this project for extracting damage skin data from MapleStory WZ files. It builds a mapping of consumable items to their associated damage skin IDs, extracts item names and descriptions from String.wz, and saves item icons alongside the damage skin assets.

### Features
- **Item Mapping**: Creates a JSON map of item IDs to damage skin IDs, including item names and descriptions.
- **Icon Extraction**: Extracts and saves item icons as PNG files in the appropriate damage skin subdirectories.
- **Asset Export**: Dumps damage skin canvas assets (images) to organized folders.
- **Backward Compatibility**: Supports assets-only mode with fallback to legacy mapping formats.

### Requirements
- .NET 8.0 or later
- MapleLib (included in the project)
- WZ files from a MapleStory installation (place in a directory accessible to the tool)

### Usage

Run the tool from the command line using the following syntax:

```
DamageSkinExtractor.exe [options]
```

#### Command-Line Options
- `-d <path>` or `--data <path>`: Path to the MapleStory WZ files directory (required).
- `-o <path>` or `--out <path>`: Output directory for extracted data (required).
- `--ids <id1,id2,...>`: Comma-separated list of specific damage skin IDs to process (optional; if not provided, processes all found IDs).
- `--mapping-only`: Only build and save the JSON mapping file, without exporting assets.
- `--assets-only`: Export assets only, using an existing mapping file in the output directory.
- `--verbose`: Enable verbose output for debugging.

#### Examples

1. **Full Extraction** (Build mapping with names/descriptions, extract skins and icons):
   ```
   DamageSkinExtractor.exe -d "C:\MapleStory\WZ" -o "C:\Output" --verbose
   ```

2. **Mapping Only** (Generate JSON map without assets):
   ```
   DamageSkinExtractor.exe -d "C:\MapleStory\WZ" -o "C:\Output" --mapping-only
   ```

3. **Assets Only** (Export skins and icons using existing mapping):
   ```
   DamageSkinExtractor.exe -d "C:\MapleStory\WZ" -o "C:\Output" --assets-only --ids 1000,1001,1002
   ```

#### Output
- **JSON Mapping**: `DamageSkinItemMap.json` in the output directory, containing item-to-damage-skin mappings with names, descriptions, and icon paths.
- **Damage Skin Assets**: Extracted to `Etc.wz/_Canvas/DamageSkin.img/<damageSkinID>/` subdirectories.
- **Item Icons**: Saved as `icon_<itemId>.png` in the respective damage skin subdirectories.

This tool relies on the same MapleLib library as the main extractor and assumes access to standard MapleStory WZ files (Item.wz, String.wz, Etc.wz, etc.).

# Technology used
- [MapleLib](https://github.com/lastbattle/MapleLib) - a very handy library for working with WZ files in C#.
