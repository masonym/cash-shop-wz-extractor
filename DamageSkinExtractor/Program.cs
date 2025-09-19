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
        private const string DefaultMaplePath = @"C:\Program Files (x86)\Steam\steamapps\common\MapleStory\Data";

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

                if (!assetsOnly)
                {
                    var map = BuildItemToDamageSkinMap(maplePath, verbose);
                    File.WriteAllText(mapPath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"Wrote mapping: {mapPath} ({map.Count} entries)");

                    if (mappingOnly)
                    {
                        return 0;
                    }

                    // If user didn’t pass --ids, use all mapped values
                    if (explicitDamageSkinIds == null)
                    {
                        explicitDamageSkinIds = new HashSet<int>(map.Values);
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
                        var existing = JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(mapPath))
                                       ?? new Dictionary<int, int>();
                        explicitDamageSkinIds = new HashSet<int>(existing.Values);
                    }
                }

                if (explicitDamageSkinIds == null || explicitDamageSkinIds.Count == 0)
                {
                    Console.WriteLine("No damage skin IDs to export.");
                    return 0;
                }

                string dsOut = Path.Combine(dumpBase, "Etc.wz", "_Canvas", "DamageSkin.img");
                DumpDamageSkinAssets(maplePath, explicitDamageSkinIds, dsOut);
                Console.WriteLine($"Export complete. Output: {dsOut}");
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
                ? Directory.GetFiles(itemDir, "*.wz")
                : Array.Empty<string>();
            if (itemWzFiles.Length == 0)
            {
                Console.WriteLine($"No Item WZ files found under {itemDir}");
                return result;
            }

            var candidateItemIds = GetCandidateDamageSkinItemIdsFromStringWz(maplePath, verbose);
            if (candidateItemIds.Count == 0)
            {
                if (verbose) Console.WriteLine("No candidate items found in String.wz/Consume.img");
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
                        if (!img.Name.Equals("Consume.img", StringComparison.OrdinalIgnoreCase))
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

            if (verbose) Console.WriteLine($"Found {result.Count} candidate items in String.wz/Consume.img.");
            return result;
        }


        private static int? TryGetDamageSkinIdFromItemCategories(IEnumerable<WzFile> itemWzFiles, int itemId, bool verbose)
        {
            string idStr = itemId.ToString();
            int prefix = itemId / 10000; // e.g., 2040001 -> 204
            string prefixStr = prefix.ToString();
            string[] categories = new[] { "Consume", "Special", "Cash" };

            foreach (var wz in itemWzFiles)
            {
                foreach (var category in categories)
                {
                    // 2a) Group file <category>/<prefix>.img containing nodes <itemId>
                    string[] groupCandidates = new[]
                    {
                        $"{category}/{prefixStr}.img",
                        $"{category}/0{prefixStr}.img",
                        $"{category}/{prefixStr}",
                        $"{category}/0{prefixStr}"
                    };
                    foreach (var groupPath in groupCandidates)
                    {
                        var obj = wz.GetObjectFromPath(groupPath);
                        if (obj is WzImage groupImg)
                        {
                            try { groupImg.ParseImage(); } catch { }
                            if (verbose) Console.WriteLine($"      Search: {category} -> {Path.GetFileName(groupImg.Name)} in {groupPath}");
                            var node = groupImg[idStr] as WzSubProperty
                                       ?? groupImg[$"0{idStr}"] as WzSubProperty;
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
                                    try { return dsProp.GetInt(); } catch { }
                                }
                            }
                        }
                        else if (obj is WzDirectory groupDir)
                        {
                            if (verbose) Console.WriteLine($"      Search directory: {category} -> {groupPath}");
                            // Look for direct image names inside this directory
                            string[] imgNames = new[]
                            {
                                $"{idStr}.img",
                                $"0{idStr}.img",
                                $"{itemId:D8}.img"
                            };
                            foreach (var name in imgNames)
                            {
                                var img = groupDir.GetImageByName(name);
                                if (img == null) continue;
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
                                    try { return dsProp.GetInt(); } catch { }
                                }
                            }
                        }
                    }
                    // 2b) Direct image paths inside <category>/
                    string[] directNames = new[]
                    {
                        $"{category}/{idStr}.img",
                        $"{category}/0{idStr}.img",
                        $"{category}/{itemId:D8}.img"
                    };
                    foreach (var path in directNames)
                    {
                        if (wz.GetObjectFromPath(path) is WzImage img)
                        {
                            if (verbose) Console.WriteLine($"      Search direct: {path}");
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
                                try { return dsProp.GetInt(); } catch { }
                            }
                        }
                    }
                }
            }
            if (verbose) Console.WriteLine($"      Not found in Consume for {itemId}");
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

            WzImage? damageSkinImg = null;
            foreach (var wzPath in etcWzFiles)
            {
                using var wz = new WzFile(wzPath, WzMapleVersion.CLASSIC);
                if (wz.ParseWzFile() != WzFileParseStatus.Success) continue;
                var img = wz.WzDirectory.GetImageByName("DamageSkin.img");
                if (img != null)
                {
                    damageSkinImg = img;
                    break;
                }
            }

            if (damageSkinImg == null)
            {
                Console.WriteLine("DamageSkin.img not found under Etc/_Canvas");
                return;
            }

            damageSkinImg.ParseImage();
            foreach (int id in damageSkinIds.Distinct())
            {
                string idStr = id.ToString();
                var node = damageSkinImg[idStr] as WzSubProperty;
                if (node == null) continue;

                string baseDir = Path.Combine(outputRoot, idStr);
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

        private static void SaveCanvasesRecursive(WzImageProperty prop, string baseDir, string currentPath)
        {
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
    }
}
