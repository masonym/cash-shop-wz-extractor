using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using MapleLib.WzLib;
using MapleLib.WzLib.WzProperties;

namespace DamageSkinExtractor
{
    internal static class Program
    {
        private const string DefaultMaplePath = @"C:\\Program Files (x86)\\Steam\\steamapps\\common\\MapleStory\\Data";

        // Extended map entry for JSON: includes name/desc and relative icon path
        private class DamageSkinItemInfo
        {
            public int ItemId { get; set; }
            public int DamageSkinId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            // Relative to dump base, e.g. "Etc.wz/_Canvas/DamageSkin.img/1000/icon_2431965.png"
            public string Icon { get; set; } = string.Empty;
        }

        private static int Main(string[] args)
        {
            // CLI
            string maplePath = DefaultMaplePath;
            string dumpBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                           "coding_projects","maple-cs-parser", "dumped_wz");
            bool mappingOnly = false;
            bool assetsOnly = false;
            bool verbose = false;
            HashSet<int>? explicitDamageSkinIds = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--":
                        // separator from dotnet run; ignore
                        break;
                    case "-h":
                    case "--help":
                        PrintHelp();
                        return 0;
                    case "-d":
                    case "--data":
                        if (i + 1 >= args.Length) return Fail("--data requires a path");
                        maplePath = args[++i];
                        break;
                    case "-o":
                    case "--out":
                        if (i + 1 >= args.Length) return Fail("--out requires a path");
                        dumpBase = args[++i];
                        break;
                    case "--mapping-only":
                        mappingOnly = true;
                        break;
                    case "--assets-only":
                        assetsOnly = true;
                        break;
                    case "--ids":
                        if (i + 1 >= args.Length) return Fail("--ids requires a comma-separated list");
                        explicitDamageSkinIds = new HashSet<int>(
                            args[++i].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                                     .Where(v => v > 0)
                        );
                        break;
                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;
                    default:
                        return Fail($"Unknown arg: {a}. Use --help for usage.");
                }
            }

            if (mappingOnly && assetsOnly)
            {
                return Fail("Cannot specify both --mapping-only and --assets-only");
            }

            // Basic startup info so the CLI shows progress even if something fails early
            Console.WriteLine("DamageSkinExtractor starting...");
            Console.WriteLine($"Data: {maplePath}");
            Console.WriteLine($"Out:  {dumpBase}");
            Console.WriteLine($"Mode: {(mappingOnly ? "mapping-only" : assetsOnly ? "assets-only" : "full")}");
            if (verbose) Console.WriteLine("Verbose: on");

            try
            {
                Directory.CreateDirectory(dumpBase);
                string mapPath = Path.Combine(dumpBase, "DamageSkinItemMap.json");
                Dictionary<int, DamageSkinItemInfo>? extendedMap = null;

                if (!assetsOnly)
                {
                    extendedMap = BuildItemToDamageSkinInfoMap(maplePath, verbose);
                    File.WriteAllText(mapPath, JsonSerializer.Serialize(extendedMap, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"Wrote mapping: {mapPath} ({extendedMap.Count} entries)");

                    if (mappingOnly)
                    {
                        return 0;
                    }

                    // If user didn’t pass --ids, use all mapped values
                    if (explicitDamageSkinIds == null)
                    {
                        explicitDamageSkinIds = new HashSet<int>(extendedMap.Values.Select(v => v.DamageSkinId));
                    }
                }
                else
                {
                    // assets-only mode must have --ids or a pre-existing mapping file
                    if (explicitDamageSkinIds == null)
                    {
                        if (!File.Exists(mapPath))
                        {
                            return Fail("--assets-only needs --ids or an existing DamageSkinItemMap.json in --out");
                        }
                        // Try extended format first
                        try
                        {
                            var loadedExt = JsonSerializer.Deserialize<Dictionary<int, DamageSkinItemInfo>>(File.ReadAllText(mapPath));
                            if (loadedExt != null && loadedExt.Count > 0)
                            {
                                extendedMap = loadedExt;
                                explicitDamageSkinIds = new HashSet<int>(loadedExt.Values.Select(v => v.DamageSkinId));
                            }
                        }
                        catch { }

                        // Fallback to legacy simple mapping
                        if (explicitDamageSkinIds == null)
                        {
                            try
                            {
                                var existing = JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(mapPath))
                                               ?? new Dictionary<int, int>();
                                explicitDamageSkinIds = new HashSet<int>(existing.Values);
                            }
                            catch
                            {
                                return Fail("Failed to parse DamageSkinItemMap.json in assets-only mode");
                            }
                        }
                    }
                }

                if (explicitDamageSkinIds == null || explicitDamageSkinIds.Count == 0)
                {
                    Console.WriteLine("No damage skin IDs to export.");
                    return 0;
                }

                string dsOut = Path.Combine(dumpBase, "Etc.wz", "_Canvas", "DamageSkin.img");
                Console.WriteLine($"Exporting damage skin assets to {dsOut}");
                DumpDamageSkinAssets(maplePath, explicitDamageSkinIds, dsOut);
                Console.WriteLine($"Export complete. Output: {dsOut}");

                // Export item icons into the corresponding damageSkinID subfolder and update the map with icon paths
                if (extendedMap == null && File.Exists(mapPath))
                {
                    try { extendedMap = JsonSerializer.Deserialize<Dictionary<int, DamageSkinItemInfo>>(File.ReadAllText(mapPath)); } catch { extendedMap = null; }
                }
                if (extendedMap != null && extendedMap.Count > 0)
                {
                    int saved = DumpItemIcons(maplePath, extendedMap, dsOut, explicitDamageSkinIds, verbose);
                    Console.WriteLine($"Saved {saved} item icons under {dsOut}");
                    // Write back the updated map (with icon relative paths)
                    File.WriteAllText(mapPath, JsonSerializer.Serialize(extendedMap, new JsonSerializerOptions { WriteIndented = true }));
                }
                return 0;
            }
            catch (Exception ex)
            {
                return Fail($"Error: {ex.Message}");
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"DamageSkinExtractor

Usage:
  DamageSkinExtractor [-d <MapleDataPath>] [-o <OutputDir>] [--mapping-only | --assets-only] [--ids <list>]

Options:
  -d, --data           Path to MapleStory Data directory (default: C:\\Program Files (x86)\\Steam\\steamapps\\common\\MapleStory\\Data)
  -o, --out            Output directory for mapping and exported PNGs (default: %USERPROFILE%\\Documents\\coding_projects\\maple-cs-parser\\dumped_wz)
  --mapping-only       Only build and write the itemId -> damageSkinID mapping JSON
  --assets-only        Only export PNGs (requires --ids or a pre-existing DamageSkinItemMap.json in --out)
  --ids                Comma/semicolon-separated list of damage skin IDs to export (e.g. ""1000,1001;1002"")
  -v, --verbose        Verbose logging
  -h, --help           Show this help
");
        }

        private static int Fail(string msg)
        {
            // Write to both stderr and stdout so callers that don't capture stderr still see messages
            Console.Error.WriteLine(msg);
            Console.WriteLine(msg);
            return 1;
        }

        // ---- Core logic (ported from the previous inline dumper) ----

        private static Dictionary<int, int> BuildItemToDamageSkinMap(string maplePath, bool verbose)
        {
            var result = new Dictionary<int, int>();

            string itemDir = Path.Combine(maplePath, "Item");
            var itemWzFiles = Directory.Exists(itemDir)
                ? Directory.GetFiles(itemDir, "*.wz", SearchOption.AllDirectories)
                : Array.Empty<string>();
            if (itemWzFiles.Length == 0)
            {
                Console.WriteLine($"No Item WZ files found under {itemDir}");
                return result;
            }

            var candidateItemIds = GetCandidateDamageSkinItemIdsFromStringWz(maplePath, verbose);
            if (candidateItemIds.Count == 0)
            {
                if (verbose) Console.WriteLine("No candidate items found in String.wz/(Consume.img, Cash.img)");
                return result;
            }
            else if (verbose)
            {
                Console.WriteLine($"Candidates count: {candidateItemIds.Count}");
                foreach (var id in candidateItemIds.Take(20))
                {
                    Console.WriteLine($"  Candidate: {id}");
                }
                if (candidateItemIds.Count > 20) Console.WriteLine("  ...");
            }

            if (verbose)
            {
                Console.WriteLine($"Scanning Item WZ files (count={itemWzFiles.Length}) for damageSkin entries...");
            }

            var parsed = new List<WzFile>();
            try
            {
                foreach (var path in itemWzFiles)
                {
                    try
                    {
                        var wz = new WzFile(path, WzMapleVersion.CLASSIC);
                        var st = wz.ParseWzFile();
                        if (st == WzFileParseStatus.Success)
                        {
                            parsed.Add(wz);
                            if (verbose) Console.WriteLine($"  Loaded {Path.GetFileName(path)}");
                        }
                        else
                        {
                            wz.Dispose();
                            if (verbose) Console.WriteLine($"  Skipped {Path.GetFileName(path)}: {st}");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.WriteLine($"  Failed to open {Path.GetFileName(path)}: {ex.Message}");
                    }
                }

                foreach (var itemId in candidateItemIds.Distinct())
                {
                    if (verbose) Console.WriteLine($"\nParsed: {parsed[0].WzDirectory.GetTopMostWzDirectory().Name}");
                    int? ds = TryGetDamageSkinIdFromItemCategories(parsed, itemId, verbose);
                    if (ds.HasValue && ds.Value > 0)
                    {
                        result[itemId] = ds.Value;
                        if (verbose) Console.WriteLine($"    Map: {itemId} -> {ds.Value}");
                    }
                    else if (verbose)
                    {
                        Console.WriteLine($"    No damageSkinID for {itemId}");
                    }
                }

                foreach (var wz in parsed)
                {
                    foreach (var img in EnumerateAllImages(wz.WzDirectory))
                    {
                        // Ensure the image is parsed before inspecting
                        try { img.ParseImage(); } catch { /* ignore parse errors per image */ }

                        string baseName = Path.GetFileNameWithoutExtension(img.Name) ?? string.Empty;
                        string trimmed = baseName.TrimStart('0');
                        bool isAllDigits = trimmed.All(char.IsDigit) && trimmed.Length > 0;

                        if (isAllDigits && trimmed.Length >= 7)
                        {
                            // Direct item image: e.g., 2431965.img
                            if (int.TryParse(trimmed, out int itemId))
                            {
                                int? ds = TryReadDamageSkinId(img);
                                if (ds.HasValue && ds.Value > 0)
                                {
                                    result[itemId] = ds.Value;
                                    if (verbose) Console.WriteLine($"    Found: item {itemId} -> damageSkinID {ds.Value} (image {img.Name})");
                                }
                            }
                        }
                        else
                        {
                            // Group image: e.g., 243.img containing nodes 2431965, etc
                            foreach (var prop in img.WzProperties)
                            {
                                if (prop is WzSubProperty node)
                                {
                                    string nodeTrim = node.Name.TrimStart('0');
                                    if (nodeTrim.Length >= 7 && nodeTrim.All(char.IsDigit) && int.TryParse(nodeTrim, out int itemId))
                                    {
                                        int? ds = TryReadDamageSkinId(node);
                                        if (ds.HasValue && ds.Value > 0)
                                        {
                                            result[itemId] = ds.Value;
                                            if (verbose) Console.WriteLine($"    Found: item {itemId} -> damageSkinID {ds.Value} (group {img.Name})");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (var wz in parsed) wz.Dispose();
            }

            if (verbose) Console.WriteLine($"Discovered {result.Count} damage skin mappings from Item WZ.");
            return result;
        }


        private static Dictionary<int, DamageSkinItemInfo> BuildItemToDamageSkinInfoMap(string maplePath, bool verbose)
        {
            // 1) Build the basic item->damageSkin map
            var basicMap = BuildItemToDamageSkinMap(maplePath, verbose);

            // 2) Read String.wz/(Consume.img, Cash.img) for name/desc
            var nameDesc = ReadItemNameDescMap(maplePath, verbose);

            // 3) Compose extended entries
            var extended = new Dictionary<int, DamageSkinItemInfo>();
            foreach (var kvp in basicMap)
            {
                int itemId = kvp.Key;
                int dsId = kvp.Value;
                nameDesc.TryGetValue(itemId, out var nd);
                extended[itemId] = new DamageSkinItemInfo
                {
                    ItemId = itemId,
                    DamageSkinId = dsId,
                    Name = nd.name ?? string.Empty,
                    Desc = nd.desc ?? string.Empty,
                    Icon = string.Empty // will be populated after we dump icons
                };
            }

            if (verbose) Console.WriteLine($"Built extended mapping with {extended.Count} entries (name/desc included)");
            return extended;
        }

        private static Dictionary<int, (string name, string desc)> ReadItemNameDescMap(string maplePath, bool verbose)
        {
            var result = new Dictionary<int, (string name, string desc)>();

            string stringDir = Path.Combine(maplePath, "String");
            var stringWzPaths = new List<string>();

            if (Directory.Exists(stringDir))
            {
                stringWzPaths.AddRange(Directory.GetFiles(stringDir, "*.wz"));
            }
            else
            {
                string singlePath = Path.Combine(maplePath, "String.wz");
                if (File.Exists(singlePath))
                {
                    stringWzPaths.Add(singlePath);
                }
            }

            if (stringWzPaths.Count == 0)
            {
                if (verbose) Console.WriteLine($"No String WZ files found under {stringDir} or {Path.Combine(maplePath, "String.wz")}.");
                return result;
            }

            var parsed = new List<WzFile>();
            try
            {
                foreach (var path in stringWzPaths)
                {
                    try
                    {
                        var wz = new WzFile(path, WzMapleVersion.CLASSIC);
                        var st = wz.ParseWzFile();
                        if (st == WzFileParseStatus.Success)
                        {
                            parsed.Add(wz);
                            if (verbose) Console.WriteLine($"  Loaded String: {Path.GetFileName(path)}");
                        }
                        else
                        {
                            wz.Dispose();
                            if (verbose) Console.WriteLine($"  Skipped String: {Path.GetFileName(path)}: {st}");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.WriteLine($"  Failed to open String WZ {Path.GetFileName(path)}: {ex.Message}");
                    }
                }

                foreach (var wz in parsed)
                {
                    foreach (var img in EnumerateAllImages(wz.WzDirectory))
                    {
                        // Pull names/descs from both Consume and Cash string tables
                        if (!img.Name.Equals("Consume.img", StringComparison.OrdinalIgnoreCase)
                            && !img.Name.Equals("Cash.img", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try { img.ParseImage(); } catch { }

                        foreach (var prop in img.WzProperties)
                        {
                            if (prop is WzSubProperty node)
                            {
                                string nodeTrim = node.Name.TrimStart('0');
                                if (!int.TryParse(nodeTrim, out int itemId)) continue;

                                string name = (node["name"] as WzStringProperty)?.GetString() ?? string.Empty;
                                string desc = (node["desc"] as WzStringProperty)?.GetString() ?? string.Empty;
                                result[itemId] = (name, desc);
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (var wz in parsed) wz.Dispose();
            }

            if (verbose) Console.WriteLine($"Read name/desc for {result.Count} items from String.wz/(Consume.img, Cash.img)");
            return result;
        }

        private static HashSet<int> GetCandidateDamageSkinItemIdsFromStringWz(string maplePath, bool verbose)
        {
            var result = new HashSet<int>();

            string stringDir = Path.Combine(maplePath, "String");
            var stringWzPaths = new List<string>();

            if (Directory.Exists(stringDir))
            {
                stringWzPaths.AddRange(Directory.GetFiles(stringDir, "*.wz"));
            }
            else
            {
                string singlePath = Path.Combine(maplePath, "String.wz");
                if (File.Exists(singlePath))
                {
                    stringWzPaths.Add(singlePath);
                }
            }

            if (stringWzPaths.Count == 0)
            {
                if (verbose) Console.WriteLine($"No String WZ files found under {stringDir} or {Path.Combine(maplePath, "String.wz")}.");
                return result;
            }

            var parsed = new List<WzFile>();
            try
            {
                foreach (var path in stringWzPaths)
                {
                    try
                    {
                        var wz = new WzFile(path, WzMapleVersion.CLASSIC);
                        var st = wz.ParseWzFile();
                        if (st == WzFileParseStatus.Success)
                        {
                            parsed.Add(wz);
                            if (verbose) Console.WriteLine($"  Loaded String: {Path.GetFileName(path)}");
                        }
                        else
                        {
                            wz.Dispose();
                            if (verbose) Console.WriteLine($"  Skipped String: {Path.GetFileName(path)}: {st}");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.WriteLine($"  Failed to open String WZ {Path.GetFileName(path)}: {ex.Message}");
                    }
                }

                foreach (var wz in parsed)
                {
                    foreach (var img in EnumerateAllImages(wz.WzDirectory))
                    {
                        if (!img.Name.Equals("Consume.img", StringComparison.OrdinalIgnoreCase)
                            && !img.Name.Equals("Cash.img", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try { img.ParseImage(); } catch { }

                        foreach (var prop in img.WzProperties)
                        {
                            if (prop is WzSubProperty node)
                            {
                                string nodeTrim = node.Name.TrimStart('0');
                                if (!int.TryParse(nodeTrim, out int itemId)) continue;

                                string name = (node["name"] as WzStringProperty)?.GetString() ?? string.Empty;
                                string desc = (node["desc"] as WzStringProperty)?.GetString() ?? string.Empty;
                                string combined = (name + " " + desc).ToLowerInvariant();

                                if (combined.Contains("damage skin"))
                                {
                                    result.Add(itemId);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (var wz in parsed) wz.Dispose();
            }

            if (verbose) Console.WriteLine($"Found {result.Count} candidate items in String.wz/(Consume.img, Cash.img).");
            return result;
        }


        // Use MapleLib's multi-file path helper across category WZ files to find group/direct images.
        private static int? TryGetDamageSkinIdFromItemCategories(IEnumerable<WzFile> itemWzFiles, int itemId, bool verbose)
        {
            var files = itemWzFiles?.ToList() ?? new List<WzFile>();
            if (files.Count == 0) return null;

            string idStr = itemId.ToString();
            string prefixStr = (itemId / 10000).ToString(); // e.g., 2040001 -> 204
            string[] categories = new[] { "Consume", "Special", "Cash" };
            string[] groupNames = new[] { $"{prefixStr}.img", $"0{prefixStr}.img" };
            string[] directNames = new[] { $"{idStr}.img", $"0{idStr}.img", $"{itemId:D8}.img" };

            foreach (var category in categories)
            {
                string catRoot = $"{category}.wz"; // e.g., Consume.wz, Cash.wz, Special.wz
                var catFiles = files.Where(f =>
                        (!string.IsNullOrEmpty(f.FilePath) &&
                         f.FilePath.IndexOf(Path.DirectorySeparatorChar + "Item" + Path.DirectorySeparatorChar + category + Path.DirectorySeparatorChar,
                                            StringComparison.OrdinalIgnoreCase) >= 0)
                        || f.Name.StartsWith(category, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                if (catFiles.Count == 0)
                {
                    if (verbose) Console.WriteLine($"      No parsed WZ file named {catRoot} loaded. Skipping category.");
                    continue;
                }

                // 1) Group image search: <category>.wz/<prefix>.img
                foreach (var gName in groupNames)
                {
                    var obj = WzFile.GetObjectFromMultipleWzFilePath($"{catRoot}/{gName}", catFiles);
                    var groupImg = obj as WzImage;
                    if (groupImg == null)
                    {
                        if (verbose) Console.WriteLine($"      Group: {category}\\{gName} (missing)");
                        continue;
                    }

                    if (verbose) Console.WriteLine($"      Group: {category}\\{gName}");
                    try { groupImg.ParseImage(); } catch { }

                    // Try direct lookup by exact/zero-padded key
                    var node = groupImg[idStr] as WzSubProperty
                               ?? groupImg[$"0{idStr}"] as WzSubProperty;

                    // Fallback: scan children and match numeric name ignoring leading zeros
                    if (node == null)
                    {
                        WzSubProperty? match = null;
                        foreach (var prop in groupImg.WzProperties)
                        {
                            if (prop is WzSubProperty sp)
                            {
                                string t = sp.Name.TrimStart('0');
                                if (t.Length >= 7 && int.TryParse(t, out int nid) && nid == itemId)
                                {
                                    match = sp;
                                    break;
                                }
                            }
                        }
                        if (match != null)
                        {
                            node = match;
                        }
                        else if (verbose)
                        {
                            var preview = string.Join(", ", groupImg.WzProperties.Take(10).Select(p => $"{p.Name}:{p.GetType().Name}"));
                            Console.WriteLine($"        No node match; child count={groupImg.WzProperties.Count}. Sample: {preview}");
                        }
                    }

                    if (node != null)
                    {
                        var info = node["info"] as WzSubProperty;
                        var dsProp = info?["damageSkinID"]
                                    ?? info?["damageSkin"]
                                    ?? node["damageSkinID"]
                                    ?? node["damageSkin"]
                                    ?? FindPropertyByName(node, "damageSkinID")
                                    ?? FindPropertyByName(node, "damageSkin");
                        if (dsProp != null)
                        {
                            var v = TryGetIntValue(dsProp);
                            if (v.HasValue) return v.Value;
                            if (verbose)
                            {
                                Console.WriteLine($"        Found property '{dsProp.Name}' of type {dsProp.GetType().Name} but couldn't parse int.");
                            }
                        }
                    }
                }

                // 2) Direct image search: <category>.wz/<id>.img (various formats)
                foreach (var fileName in directNames)
                {
                    var obj = WzFile.GetObjectFromMultipleWzFilePath($"{catRoot}/{fileName}", catFiles);
                    var img = obj as WzImage;
                    if (img == null)
                    {
                        if (verbose) Console.WriteLine($"      Search direct: {category}\\{fileName} (missing)");
                        continue;
                    }
                    if (verbose) Console.WriteLine($"      Search direct: {category}\\{fileName}");
                    try { img.ParseImage(); } catch { }
                    var info = img["info"] as WzSubProperty;
                    var dsProp = info?["damageSkinID"]
                                ?? info?["damageSkin"]
                                ?? img["damageSkinID"]
                                ?? img["damageSkin"]
                                ?? FindPropertyByName(img, "damageSkinID")
                                ?? FindPropertyByName(img, "damageSkin");
                    if (dsProp != null)
                    {
                        var v = TryGetIntValue(dsProp);
                        if (v.HasValue) return v.Value;
                        if (verbose)
                        {
                            Console.WriteLine($"        Found property '{dsProp.Name}' of type {dsProp.GetType().Name} but couldn't parse int.");
                        }
                    }
                    else if (verbose)
                    {
                        var infoPreview = info == null
                            ? "(no info node)"
                            : string.Join(", ", info.WzProperties.Select(p => p.Name).Take(12));
                        Console.WriteLine($"        No damageSkin property in info. info children: {infoPreview}");
                    }
                }
            }

            if (verbose) Console.WriteLine($"      Not found for {itemId}");
            return null;
        }

        private static int? TryGetIntValue(WzImageProperty? prop)
        {
            if (prop == null) return null;
            try
            {
                return prop.GetInt();
            }
            catch { }

            if (prop is WzStringProperty ws)
            {
                var s = ws.GetString();
                if (int.TryParse(s, out var v)) return v;
            }
            return null;
        }

        private static IEnumerable<WzImage> EnumerateAllImages(WzDirectory dir)
        {
            foreach (var img in dir.WzImages)
                yield return img;
            foreach (var sub in dir.WzDirectories)
            {
                foreach (var img in EnumerateAllImages(sub))
                    yield return img;
            }
        }

        private static int? TryReadDamageSkinId(WzImage image)
        {
            // Prefer info/damageSkinID or info/damageSkin, fallback to any occurrence
            var info = image["info"] as WzSubProperty;
            var cand = (info != null ? (info["damageSkinID"] ?? info["damageSkin"]) : null)
                       ?? image["damageSkinID"]
                       ?? image["damageSkin"]
                       ?? FindPropertyByName(image, "damageSkinID")
                       ?? FindPropertyByName(image, "damageSkin");
            if (cand == null) return null;
            try { return cand.GetInt(); } catch { return null; }
        }

        private static int? TryReadDamageSkinId(WzSubProperty node)
        {
            var info = node["info"] as WzSubProperty;
            var cand = (info != null ? (info["damageSkinID"] ?? info["damageSkin"]) : null)
                       ?? node["damageSkinID"]
                       ?? node["damageSkin"]
                       ?? FindPropertyByName(node, "damageSkinID")
                       ?? FindPropertyByName(node, "damageSkin");
            if (cand == null) return null;
            try { return cand.GetInt(); } catch { return null; }
        }

        private static void DumpDamageSkinAssets(string maplePath, IEnumerable<int> damageSkinIds, string outputRoot)
        {
            // 3) Locate Etc/_Canvas/DamageSkin.img and export canvases for the IDs we discovered
            string etcCanvasDir = Path.Combine(maplePath, "Etc", "_Canvas");
            var etcWzFiles = Directory.Exists(etcCanvasDir)
                ? Directory.GetFiles(etcCanvasDir, "*.wz")
                : Array.Empty<string>();
            if (etcWzFiles.Length == 0)
            {
                Console.WriteLine($"No Etc/_Canvas/*.wz found under {etcCanvasDir}");
                return;
            }
            Console.WriteLine($"Found {etcWzFiles.Length} Etc/_Canvas/*.wz files");

            WzImage? damageSkinImg = null;
            WzFile? damageSkinWzFile = null;
            foreach (var wzPath in etcWzFiles)
            {
                var wz = new WzFile(wzPath, WzMapleVersion.CLASSIC);
                if (wz.ParseWzFile() != WzFileParseStatus.Success)
                {
                    wz.Dispose();
                    continue;
                }
                var img = wz.WzDirectory.GetImageByName("DamageSkin.img");
                if (img != null)
                {
                    Console.WriteLine($"DamageSkin.img found in {wzPath}. Continuing...");
                    damageSkinImg = img;
                    damageSkinWzFile = wz; // Keep the WZ file alive
                    break;
                }
                else
                {
                    Console.WriteLine($"DamageSkin.img not found in {wzPath}");
                    wz.Dispose();
                }
            }

            if (damageSkinImg == null || damageSkinWzFile == null)
            {
                Console.WriteLine("DamageSkin.img not found under Etc/_Canvas");
                return;
            }

            try
            {
                Console.WriteLine($"Found DamageSkin.img {damageSkinImg}");

                foreach (int id in damageSkinIds.Distinct())
                {
                    string idStr = id.ToString();
                    Console.WriteLine($"Processing DamageSkin {id} in {damageSkinImg}");
                    var node = damageSkinImg[idStr] as WzSubProperty;
                    if (node == null)
                    {
                        Console.WriteLine($"DamageSkin {id} not found");
                        continue;
                    }

                    string baseDir = Path.Combine(outputRoot, idStr);
                    Console.WriteLine($"Exporting DamageSkin {id} to {baseDir}");
                    try
                    {
                        Directory.CreateDirectory(baseDir);
                        SaveCanvasesRecursive(node, baseDir, "");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed exporting DamageSkin {id}: {ex.Message}");
                    }
                }
            }
            finally
            {
                // Clean up the WZ file when done
                damageSkinWzFile?.Dispose();
            }
        }

        private static void SaveCanvasesRecursive(WzImageProperty prop, string baseDir, string currentPath)
        {
            Console.WriteLine($"Saving canvas {prop.FullPath}");
            if (prop is WzCanvasProperty canvas)
            {
                try
                {
                    using var bmp = canvas.PngProperty?.GetImage(false);
                    if (bmp != null)
                    {
                        string dir = string.IsNullOrEmpty(currentPath) ? baseDir : Path.Combine(baseDir, currentPath);
                        Directory.CreateDirectory(dir);
                        string outPath = Path.Combine(dir, canvas.Name + ".png");
                        bmp.Save(outPath, ImageFormat.Png);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving canvas {prop.FullPath}: {ex.Message}");
                }
            }
            else if (prop is WzSubProperty sub)
            {
                string nextPath = string.IsNullOrEmpty(currentPath) ? sub.Name : Path.Combine(currentPath, sub.Name);
                foreach (var child in sub.WzProperties)
                {
                    SaveCanvasesRecursive(child, baseDir, nextPath);
                }
            }
            else if (prop is WzConvexProperty extended)
            {
                string nextPath = string.IsNullOrEmpty(currentPath) ? extended.Name : Path.Combine(currentPath, extended.Name);
                foreach (var child in extended.WzProperties)
                {
                    SaveCanvasesRecursive(child, baseDir, nextPath);
                }
            }
        }

        private static WzImageProperty? FindPropertyByName(WzImage image, string targetName)
        {
            foreach (var prop in image.WzProperties)
            {
                var found = FindPropertyByName(prop, targetName);
                if (found != null) return found;
            }
            return null;
        }

        private static WzImageProperty? FindPropertyByName(WzImageProperty prop, string targetName)
        {
            if (prop.Name == targetName) return prop;
            if (prop is WzSubProperty sub)
            {
                foreach (var child in sub.WzProperties)
                {
                    var found = FindPropertyByName(child, targetName);
                    if (found != null) return found;
                }
            }
            else if (prop is WzConvexProperty ext)
            {
                foreach (var child in ext.WzProperties)
                {
                    var found = FindPropertyByName(child, targetName);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // ---- Icon extraction (Item/Consume, Item/Cash, Item/Special) ----

        private static int DumpItemIcons(string maplePath,
                                         Dictionary<int, DamageSkinItemInfo> extendedMap,
                                         string dsOut,
                                         HashSet<int> onlyDamageSkinIds,
                                         bool verbose)
        {
            // Determine dumpBase from dsOut = <dumpBase>/Etc.wz/_Canvas/DamageSkin.img
            var dumpBaseDir = new DirectoryInfo(dsOut).Parent?.Parent?.Parent;
            if (dumpBaseDir == null)
            {
                if (verbose) Console.WriteLine($"Failed to resolve dump base from path: {dsOut}");
                return 0;
            }

            // Load Item/*/*.wz and Item/*/_Canvas/*.wz across relevant categories
            string consumeDir = Path.Combine(maplePath, "Item", "Consume");
            string cashDir = Path.Combine(maplePath, "Item", "Cash");
            string specialDir = Path.Combine(maplePath, "Item", "Special");

            var consumeWzFiles = Directory.Exists(consumeDir) ? Directory.GetFiles(consumeDir, "*.wz", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var cashWzFiles = Directory.Exists(cashDir) ? Directory.GetFiles(cashDir, "*.wz", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var specialWzFiles = Directory.Exists(specialDir) ? Directory.GetFiles(specialDir, "*.wz", SearchOption.TopDirectoryOnly) : Array.Empty<string>();

            var consumeCanvasWzFiles = Directory.Exists(Path.Combine(consumeDir, "_Canvas")) ? Directory.GetFiles(Path.Combine(consumeDir, "_Canvas"), "*.wz", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var cashCanvasWzFiles = Directory.Exists(Path.Combine(cashDir, "_Canvas")) ? Directory.GetFiles(Path.Combine(cashDir, "_Canvas"), "*.wz", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var specialCanvasWzFiles = Directory.Exists(Path.Combine(specialDir, "_Canvas")) ? Directory.GetFiles(Path.Combine(specialDir, "_Canvas"), "*.wz", SearchOption.TopDirectoryOnly) : Array.Empty<string>();

            if (consumeWzFiles.Length == 0 && cashWzFiles.Length == 0 && specialWzFiles.Length == 0)
            {
                if (verbose) Console.WriteLine($"No Item WZ files found in Item/Consume, Item/Cash, or Item/Special");
                return 0;
            }

            var parsedItems = new List<WzFile>();
            var parsedCanvas = new List<WzFile>();
            try
            {
                foreach (var p in consumeWzFiles.Concat(cashWzFiles).Concat(specialWzFiles))
                {
                    try
                    {
                        var wz = new WzFile(p, WzMapleVersion.CLASSIC);
                        if (wz.ParseWzFile() == WzFileParseStatus.Success) parsedItems.Add(wz); else wz.Dispose();
                    }
                    catch { }
                }
                foreach (var p in consumeCanvasWzFiles.Concat(cashCanvasWzFiles).Concat(specialCanvasWzFiles))
                {
                    try
                    {
                        var wz = new WzFile(p, WzMapleVersion.CLASSIC);
                        if (wz.ParseWzFile() == WzFileParseStatus.Success) parsedCanvas.Add(wz); else wz.Dispose();
                    }
                    catch { }
                }

                int saved = 0;
                foreach (var kvp in extendedMap)
                {
                    int itemId = kvp.Key;
                    int dsId = kvp.Value.DamageSkinId;
                    if (onlyDamageSkinIds != null && onlyDamageSkinIds.Count > 0 && !onlyDamageSkinIds.Contains(dsId))
                        continue;

                    string relIconPath = Path.Combine("Etc.wz", "_Canvas", "DamageSkin.img", dsId.ToString(), $"icon_{itemId}.png");
                    string fullIconPath = Path.Combine(dumpBaseDir.FullName, relIconPath);

                    if (TrySaveItemIconFromItems(parsedItems, parsedCanvas, itemId, fullIconPath, verbose))
                    {
                        kvp.Value.Icon = relIconPath.Replace('\\', '/');
                        saved++;
                    }
                    else if (File.Exists(fullIconPath))
                    {
                        // Icon already exists from a previous run; still update the mapping
                        kvp.Value.Icon = relIconPath.Replace('\\', '/');
                    }
                    else
                    {
                        // Fallback: some DamageSkin entries have their own icon canvas exported by DumpDamageSkinAssets
                        // e.g., <dumpBase>/Etc.wz/_Canvas/DamageSkin.img/<dsId>/icon.png
                        string dsDir = Path.Combine(dumpBaseDir.FullName, "Etc.wz", "_Canvas", "DamageSkin.img", dsId.ToString());
                        string dsIconFull = Path.Combine(dsDir, "icon.png");
                        if (File.Exists(dsIconFull))
                        {
                            string dsIconRel = Path.Combine("Etc.wz", "_Canvas", "DamageSkin.img", dsId.ToString(), "icon.png").Replace('\\', '/');
                            kvp.Value.Icon = dsIconRel;
                        }
                    }
                }
                return saved;
            }
            finally
            {
                foreach (var wz in parsedItems) wz.Dispose();
                foreach (var wz in parsedCanvas) wz.Dispose();
            }
        }

        private static bool TrySaveItemIconFromItems(List<WzFile> itemFiles, List<WzFile> canvasFiles, int itemId, string pngFullPath, bool verbose)
        {
            string idStr = itemId.ToString();
            string prefixStr = (itemId / 10000).ToString();
            string[] groupNames = new[] { $"{prefixStr}.img", $"0{prefixStr}.img" };
            string[] directNames = new[] { $"{idStr}.img", $"0{idStr}.img", $"{itemId:D8}.img" };

            // 1) Try direct images
            foreach (var wz in itemFiles)
            {
                var img = TryGetImageByNames(wz.WzDirectory, directNames);
                if (img != null)
                {
                    try
                    {
                        img.ParseImage();
                        var info = img["info"] as WzSubProperty;
                        if (info != null && TrySaveIconFromContainer(info, canvasFiles, pngFullPath, verbose)) return true;
                    }
                    catch { }
                }
            }

            // 2) Try group images and node lookup
            foreach (var wz in itemFiles)
            {
                foreach (var g in groupNames)
                {
                    var img = wz.WzDirectory.GetImageByName(g);
                    if (img == null) continue;
                    try { img.ParseImage(); } catch { }
                    var node = img[idStr] as WzSubProperty ?? img[$"0{idStr}"] as WzSubProperty;
                    if (node == null)
                    {
                        foreach (var p in img.WzProperties)
                        {
                            if (p is WzSubProperty sp)
                            {
                                string t = sp.Name.TrimStart('0');
                                if (int.TryParse(t, out int nid) && nid == itemId) { node = sp; break; }
                            }
                        }
                    }
                    if (node == null) continue;

                    var info = node["info"] as WzSubProperty;
                    if (info != null && TrySaveIconFromContainer(info, canvasFiles, pngFullPath, verbose)) return true;
                    if (TrySaveIconFromContainer(node, canvasFiles, pngFullPath, verbose)) return true;
                }
            }

            if (verbose) Console.WriteLine($"  No icon found for item {itemId}");
            return false;
        }

        private static bool TrySaveIconFromContainer(WzSubProperty container, List<WzFile> canvasFiles, string pngFullPath, bool verbose)
        {
            foreach (var name in new[] { "iconRaw", "icon" })
            {
                var prop = container[name];
                if (prop is WzCanvasProperty canv)
                {
                    if (TrySaveCanvas(canv, canvasFiles, pngFullPath, verbose)) return true;
                }
                else if (prop is WzUOLProperty uol)
                {
                    try
                    {
                        var linked = uol.LinkValue;
                        if (linked is WzCanvasProperty canv2)
                        {
                            if (TrySaveCanvas(canv2, canvasFiles, pngFullPath, verbose)) return true;
                        }
                        else if (linked is WzSubProperty sub && sub[name] is WzCanvasProperty canv3)
                        {
                            if (TrySaveCanvas(canv3, canvasFiles, pngFullPath, verbose)) return true;
                        }
                    }
                    catch { }
                }
                else if (prop is WzSubProperty sub2 && sub2[name] is WzCanvasProperty canv4)
                {
                    if (TrySaveCanvas(canv4, canvasFiles, pngFullPath, verbose)) return true;
                }
            }
            return false;
        }

        private static bool TrySaveCanvas(WzCanvasProperty canvasProp, List<WzFile> canvasFiles, string pngFullPath, bool verbose)
        {
            try
            {
                var linkProp = canvasProp["_outlink"] as WzStringProperty ?? canvasProp["_inlink"] as WzStringProperty;
                WzCanvasProperty finalCanvas = canvasProp;

                if (linkProp != null)
                {
                    var linked = linkProp.GetLinkedWzImageProperty();
                    if (linked is WzCanvasProperty linkedCanvas)
                    {
                        finalCanvas = linkedCanvas;
                    }
                    else
                    {
                        string linkStr = linked?.WzValue?.ToString() ?? linkProp.Value;
                        if (!string.IsNullOrEmpty(linkStr))
                        {
                            string[] parts = linkStr.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            int imgIdx = Array.FindIndex(parts, p => p.EndsWith(".img", StringComparison.OrdinalIgnoreCase));
                            if (imgIdx >= 0)
                            {
                                string imgName = parts[imgIdx];
                                var canvasImage = GetCanvasImage(canvasFiles, imgName);
                                if (canvasImage != null)
                                {
                                    try { canvasImage.ParseImage(); } catch { }
                                    string[] innerPath = parts.Skip(imgIdx + 1).ToArray();
                                    var targetProp = FindPropertyByPath(canvasImage, innerPath);
                                    if (targetProp is WzCanvasProperty targetCanvas)
                                    {
                                        finalCanvas = targetCanvas;
                                    }
                                    else if (targetProp is WzSubProperty maybeInfo)
                                    {
                                        if (maybeInfo[canvasProp.Name] is WzCanvasProperty childCanvas)
                                        {
                                            finalCanvas = childCanvas;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                using (var bmp = finalCanvas.PngProperty?.GetImage(false))
                {
                    if (bmp == null) return false;
                    Directory.CreateDirectory(Path.GetDirectoryName(pngFullPath)!);
                    bmp.Save(pngFullPath, ImageFormat.Png);
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (verbose) Console.WriteLine($"    Failed saving canvas to {pngFullPath}: {ex.Message}");
                return false;
            }
        }

        private static WzImage? GetCanvasImage(List<WzFile> canvasFiles, string imageName)
        {
            foreach (var f in canvasFiles)
            {
                var img = f.WzDirectory.GetImageByName(imageName);
                if (img != null) return img;
            }
            return null;
        }

        private static WzImage? TryGetImageByNames(WzDirectory dir, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var img = dir.GetImageByName(name);
                if (img != null) return img;
            }
            foreach (var sub in dir.WzDirectories)
            {
                var found = TryGetImageByNames(sub, names);
                if (found != null) return found;
            }
            return null;
        }

        private static WzImageProperty? FindPropertyByPath(WzImage image, IEnumerable<string> segments)
        {
            if (image == null) return null;
            IEnumerable<WzImageProperty> currentList = image.WzProperties;
            WzImageProperty? current = null;
            foreach (var raw in segments)
            {
                var seg = raw?.Trim();
                if (string.IsNullOrEmpty(seg)) continue;
                WzImageProperty? next = null;
                foreach (var p in currentList)
                {
                    if (p.Name.Equals(seg, StringComparison.OrdinalIgnoreCase)) { next = p; break; }
                }
                if (next == null) return current;
                current = next;
                if (current is WzSubProperty sp) currentList = sp.WzProperties;
                else if (current is WzConvexProperty cp) currentList = cp.WzProperties;
                else currentList = Array.Empty<WzImageProperty>();
            }
            return current;
        }
    }
}
