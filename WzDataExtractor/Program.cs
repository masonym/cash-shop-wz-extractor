using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Xml;
using MapleLib.WzLib;
using MapleLib.WzLib.WzProperties;
using MapleLib.WzLib.WzStructure.Data.ItemStructure;
using static System.Net.Mime.MediaTypeNames;
using SharpDX.DirectWrite;
using MapleLib.WzLib.Serialization;
using System.Xml.Serialization;
using SharpDX.MediaFoundation.DirectX;
using static System.Windows.Forms.LinkLabel;

namespace WzDataExtractor
{
    public class CanvasManager
    {
        private Dictionary<string, List<WzFile>> canvasFiles = new Dictionary<string, List<WzFile>>();

        public void AddCanvasFile(string category, string filePath)
        {
            Console.WriteLine($"Adding canvas file for category {category}: {filePath}");
            if (File.Exists(filePath))
            {
                WzFile canvasWzFile = new WzFile(filePath, WzMapleVersion.CLASSIC);
                canvasWzFile.ParseWzFile();
                if (!canvasFiles.ContainsKey(category))
                {
                    canvasFiles[category] = new List<WzFile>();
                }
                canvasFiles[category].Add(canvasWzFile);
                Console.WriteLine($"Successfully added canvas file for {category}");
                //PrintWzFileContents(canvasWzFile);
            }
            else
            {
                Console.WriteLine($"Canvas file not found: {filePath}");
            }
        }

        public WzImage GetCanvasImage(string category, string imageName)
        {
            if (canvasFiles.TryGetValue(category, out List<WzFile> categoryFiles))
            {
                foreach (WzFile canvasFile in categoryFiles)
                {
                    WzImage image = canvasFile.WzDirectory.GetImageByName(imageName);
                    if (image != null)
                    {
                        return image;
                    }
                }
            }
            return null;
        }

        private void PrintWzFileContents(WzFile file)
        {
            Console.WriteLine($"Contents of {file.Name}:");
            foreach (var image in file.WzDirectory.WzImages)
            {
                Console.WriteLine($"  - {image.Name}");
            }
        }

    }


    public class CharacterWzDumper
    {
        private static XmlWriterSettings XmlSettings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            Encoding = System.Text.Encoding.UTF8
        };

        public static void DumpCharacterWzData(string characterWzPath, List<int> itemIds, string outputPath)
        {
            var allFiles = GetWzFilesInFolder(characterWzPath);
            var groupedFiles = GroupWzFilesByCategory(allFiles);
            var canvasManager = new CanvasManager();

            foreach (var category in groupedFiles.Keys)
            {
                Console.WriteLine($"Processing category: {category}");

                // Add canvas files to the manager
                foreach (var canvasFile in groupedFiles[category].CanvasFiles)
                {
                    Console.WriteLine($"Adding canvas file: {canvasFile}");
                    canvasManager.AddCanvasFile(category, canvasFile);
                }

                // Process main files
                foreach (var file in groupedFiles[category].MainFiles)
                {
                    Console.WriteLine($"Processing file: {file}");
                    using (WzFile wzFile = new WzFile(file, WzMapleVersion.CLASSIC))
                    {
                        wzFile.ParseWzFile();

                        foreach (int itemId in itemIds)
                        {
                            WzImage itemImg = FindItemImageRecursive(wzFile.WzDirectory, itemId);
                            if (itemImg != null)
                            {
                                string subfolder = GetCharacterWzSubfolder(itemId);
                                DumpItemData(itemImg, itemId, Path.Combine(outputPath, subfolder), category, canvasManager);
                            }
                        }
                    }
                }
            }

            //canvasManager.Dispose();
        }

        private static string CleanFileName(string fileName)
        {
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        }

        private static Dictionary<string, (List<string> CanvasFiles, List<string> MainFiles)> GroupWzFilesByCategory(List<string> files)
        {
            var groupedFiles = new Dictionary<string, (List<string> CanvasFiles, List<string> MainFiles)>();

            foreach (var file in files)
            {
                string directory = Path.GetDirectoryName(file);
                string category = Path.GetFileName(directory);
                bool isCanvas = false;

                if (category == "_Canvas")
                {
                    category = Path.GetFileName(Path.GetDirectoryName(directory));
                    isCanvas = true;
                }

                if (!groupedFiles.ContainsKey(category))
                {
                    groupedFiles[category] = (new List<string>(), new List<string>());
                }

                if (isCanvas)
                {
                    groupedFiles[category].CanvasFiles.Add(file);
                }
                else
                {
                    groupedFiles[category].MainFiles.Add(file);
                }
            }

            return groupedFiles;
        }

        private static List<string> GetWzFilesInFolder(string path)
        {
            return Directory.GetFiles(path, "*.wz", SearchOption.AllDirectories).ToList();
        }

        private static WzImage FindItemImageRecursive(WzDirectory directory, int itemId)
        {
            foreach (WzImage img in directory.WzImages)
            {
                if (img.Name.StartsWith(itemId.ToString("D8")))
                {
                    return img;
                }
            }

            foreach (WzDirectory subDir in directory.WzDirectories)
            {
                WzImage result = FindItemImageRecursive(subDir, itemId);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static string GetCharacterWzSubfolder(int itemId)
        {
            int category = itemId / 10000;
            return category switch
            {
                >= 2 and <= 5 => "Face",
                100 => "Cap",
                >= 101 and <= 103 or >= 112 and <= 119 => "Accessory",
                104 => "Coat",
                105 => "Longcoat",
                106 => "Pants",
                107 => "Shoes",
                108 => "Glove",
                109 => "Shield",
                110 => "Cape",
                111 => "Ring",
                >= 166 and <= 166 => "Android",
                180 => "PetEquip",
                >= 121 and <= 171 => "Weapon",
                _ => "Etc"
            };
        }

        private static void DumpItemData(WzImage itemImg, int itemId, string outputPath, string category, CanvasManager canvasManager)
        {
            string itemFolder = Path.Combine(outputPath, $"{itemId:D8}.img");
            Directory.CreateDirectory(itemFolder);

            string xmlPath = Path.Combine(outputPath, $"{itemId:D8}.img.xml");
            using (StreamWriter sw = new StreamWriter(xmlPath))
            using (XmlWriter xmlWriter = XmlWriter.Create(sw, XmlSettings))
            {
                xmlWriter.WriteStartDocument();
                xmlWriter.WriteStartElement("imgdir");
                xmlWriter.WriteAttributeString("name", itemImg.Name);

                foreach (WzImageProperty prop in itemImg.WzProperties)
                {
                    DumpProperty(xmlWriter, prop, itemFolder, "", category, canvasManager);
                }

                xmlWriter.WriteEndElement();
                xmlWriter.WriteEndDocument();
            }
        }

        private static void DumpProperty(XmlWriter xmlWriter, WzImageProperty prop, string outputPath, string currentPath, string category, CanvasManager canvasManager)
        {
            switch (prop.PropertyType)
            {
                case WzPropertyType.Canvas:
                    DumpCanvasProperty(xmlWriter, (WzCanvasProperty)prop, outputPath, currentPath, category, canvasManager);
                    break;
                case WzPropertyType.Vector:
                    DumpVectorProperty(xmlWriter, (WzVectorProperty)prop);
                    break;
                case WzPropertyType.Convex:
                    DumpConvexProperty(xmlWriter, (WzConvexProperty)prop, outputPath, currentPath, category, canvasManager);
                    break;
                case WzPropertyType.SubProperty:
                    DumpSubProperty(xmlWriter, (WzSubProperty)prop, outputPath, currentPath, category, canvasManager);
                    break;
                case WzPropertyType.Sound:
                    //DumpSoundProperty(xmlWriter, (WzSoundProperty)prop, outputPath, currentPath);
                    break;
                case WzPropertyType.UOL:
                    DumpUOLProperty(xmlWriter, (WzUOLProperty)prop);
                    break;
                default:
                    DumpSimpleProperty(xmlWriter, prop);
                    break;
            }
        }


        private static void DumpCanvasProperty(XmlWriter xmlWriter, WzCanvasProperty canvasProp, string outputPath, string currentPath, string category, CanvasManager canvasManager)
        {
            xmlWriter.WriteStartElement("canvas");
            xmlWriter.WriteAttributeString("name", canvasProp.Name);

            Console.WriteLine($"Processing canvas: {canvasProp.Name}");
            Console.WriteLine($"Current category: {category}");

            string fileName = CleanFileName(canvasProp.ParentImage.Name);
            string result = fileName;
            if (fileName == "01041001.img")
            {
                Console.WriteLine("----------------------------------------------------");
            }

            Console.WriteLine($"File name: {fileName}");
            string canvasCategoryOutputPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)), category);
            string canvasOutputPath = Path.Combine(canvasCategoryOutputPath, "_Canvas");
            string pngRelativePath = Path.Combine(category, "_Canvas", result, currentPath, canvasProp.Name + ".png");
            string pngFullPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)), pngRelativePath);

            WzStringProperty linkProp = (WzStringProperty)canvasProp["_outlink"] ?? (WzStringProperty)canvasProp["_inlink"];

            if (linkProp != null) {
                Console.WriteLine($"--------linkprop value ---------{linkProp.Value}");
                Console.WriteLine($"---canvas parent--------{canvasProp.GetTopMostWzDirectory().Name}");
                WzImageProperty linkedProp = linkProp.GetLinkedWzImageProperty();
                if (linkedProp != null) {
                    Console.WriteLine($"----asdasd----linkedprop value ---------{linkedProp.WzValue}");
                    string linkPropString = linkedProp.WzValue.ToString();
                    string[] parts = linkPropString.Split('/');
                    result = parts.FirstOrDefault(part => part.EndsWith(".img"));
                    pngRelativePath = Path.Combine(category, "_Canvas", result, currentPath, canvasProp.Name + ".png");
                    pngFullPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)), pngRelativePath);
                    Console.WriteLine($"Link prop string: {result}");
                    
                }


            }



            /*
            Console.WriteLine($"Output path: {outputPath}");
            Console.WriteLine($"Canvas category Path: {canvasCategoryOutputPath}");
            Console.WriteLine($"Canvas output Path: {canvasOutputPath}");
            Console.WriteLine($"Png Relative Path: {pngRelativePath}");
            Console.WriteLine($"Png Full Path: {pngFullPath}");
            */
            WzImage canvasImage = canvasManager.GetCanvasImage(category, result);
            if (canvasImage != null)
            {
                Console.WriteLine($"Found canvas image: {canvasImage.FullPath}");

                WzCanvasProperty canvasProperty = FindCanvasProperty(canvasImage, canvasProp.Name);
                if (canvasProperty != null)
                {
                    try
                    {
                        using (Bitmap bmp = canvasProperty.PngProperty.GetImage(false))
                        {
                            if (bmp != null)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(pngFullPath));
                                bmp.Save(pngFullPath, System.Drawing.Imaging.ImageFormat.Png);
                                Console.WriteLine($"Saved image to: {pngFullPath}");
                                xmlWriter.WriteAttributeString("width", bmp.Width.ToString());
                                xmlWriter.WriteAttributeString("height", bmp.Height.ToString());
                                xmlWriter.WriteAttributeString("png", pngRelativePath);
                            }
                            else
                            {
                                Console.WriteLine("Failed to get bitmap from PNG property.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error saving image: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Canvas property '{canvasProp.Name}' not found in the canvas image.");
                }
            }
            else
            {
                Console.WriteLine($"Canvas image not found for category: {category}, file: {fileName}");
            }

            foreach (WzImageProperty subProp in canvasProp.WzProperties)
            {
                DumpProperty(xmlWriter, subProp, outputPath, Path.Combine(currentPath, canvasProp.Name), category, canvasManager);
            }

            xmlWriter.WriteEndElement();
        }

        private static WzCanvasProperty FindCanvasProperty(WzImage image, string propertyName)
        {
            foreach (WzImageProperty prop in image.WzProperties)
            {
                if (prop is WzCanvasProperty canvasProp && canvasProp.Name == propertyName)
                {
                    return canvasProp;
                }
                else if (prop is WzSubProperty subProp)
                {
                    WzCanvasProperty result = FindCanvasPropertyInSubProperty(subProp, propertyName);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }

        private static WzCanvasProperty FindCanvasPropertyInSubProperty(WzSubProperty subProp, string propertyName)
        {
            foreach (WzImageProperty prop in subProp.WzProperties)
            {
                if (prop is WzCanvasProperty canvasProp && canvasProp.Name == propertyName)
                {
                    return canvasProp;
                }
                else if (prop is WzSubProperty nestedSubProp)
                {
                    WzCanvasProperty result = FindCanvasPropertyInSubProperty(nestedSubProp, propertyName);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }


        private static void DumpConvexProperty(XmlWriter xmlWriter, WzConvexProperty convexProp, string outputPath, string currentPath, string category, CanvasManager canvasManager)
        {
            xmlWriter.WriteStartElement("extended");
            xmlWriter.WriteAttributeString("name", convexProp.Name);
            foreach (WzImageProperty subProp in convexProp.WzProperties)
            {
                DumpProperty(xmlWriter, subProp, outputPath, Path.Combine(currentPath, convexProp.Name), category, canvasManager);
            }
            xmlWriter.WriteEndElement();
        }

        private static void DumpSubProperty(XmlWriter xmlWriter, WzSubProperty subProp, string outputPath, string currentPath, string category, CanvasManager canvasManager)
        {
            xmlWriter.WriteStartElement("imgdir");
            xmlWriter.WriteAttributeString("name", subProp.Name);
            foreach (WzImageProperty childProp in subProp.WzProperties)
            {
                DumpProperty(xmlWriter, childProp, outputPath, Path.Combine(currentPath, subProp.Name), category, canvasManager);
            }
            xmlWriter.WriteEndElement();
        }

        /*
        private static void DumpSoundProperty(XmlWriter xmlWriter, WzSoundProperty soundProp, string outputPath)
        {
            xmlWriter.WriteStartElement("sound");
            xmlWriter.WriteAttributeString("name", soundProp.Name);
            string soundFileName = $"{soundProp.Name}.mp3";
            string soundPath = Path.Combine(outputPath, soundFileName);
            File.WriteAllBytes(soundPath, soundProp.GetBytes(false));
            xmlWriter.WriteAttributeString("file", soundFileName);
            xmlWriter.WriteEndElement();
        }
        */
        private static void DumpVectorProperty(XmlWriter xmlWriter, WzVectorProperty vectorProp)
        {
            xmlWriter.WriteStartElement("vector");
            xmlWriter.WriteAttributeString("name", vectorProp.Name);
            xmlWriter.WriteAttributeString("x", vectorProp.X.Value.ToString());
            xmlWriter.WriteAttributeString("y", vectorProp.Y.Value.ToString());
            xmlWriter.WriteEndElement();
        }

        private static void DumpUOLProperty(XmlWriter xmlWriter, WzUOLProperty uolProp)
        {
            xmlWriter.WriteStartElement("uol");
            xmlWriter.WriteAttributeString("name", uolProp.Name);
            xmlWriter.WriteAttributeString("value", uolProp.Value);
            xmlWriter.WriteEndElement();
        }

        private static void DumpSimpleProperty(XmlWriter xmlWriter, WzImageProperty prop)
        {
            xmlWriter.WriteStartElement(prop.PropertyType.ToString().ToLower());
            xmlWriter.WriteAttributeString("name", prop.Name);
            xmlWriter.WriteAttributeString("value", prop.ToString());
            xmlWriter.WriteEndElement();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            string etcWzPath = @"WzFiles\Etc\Etc_000.wz";
            string characterWzPath = @"WzFiles\Character\Character_000.wz";
            //string accessoryWzPath = @"WzFiles\Character\Accessory\Accessory_000.wz";
            string stringWzPath = @"WzFiles\String\String_000.wz";
            string itemWzPath = @"WzFiles\Item\Item_000.wz";
            
            WzFile etcWz = new WzFile(etcWzPath, WzMapleVersion.CLASSIC);
            WzFile characterWz = new WzFile(characterWzPath, WzMapleVersion.CLASSIC);
            WzFile stringWz = new WzFile(stringWzPath, WzMapleVersion.CLASSIC);
            WzFile itemWz = new WzFile(itemWzPath, WzMapleVersion.CLASSIC);

            //WzFile accessoryWz = new WzFile(accessoryWzPath, WzMapleVersion.CLASSIC);

            characterWz.ParseWzFile();
            stringWz.ParseWzFile();
            itemWz.ParseWzFile();

            string outputPath = "output";

            //ExtractWzFile(etcWzPath, outputPath, WzMapleVersion.CLASSIC);
            //ExtractWzFile(stringWzPath, outputPath, WzMapleVersion.CLASSIC);
            //ExtractItemDirectory(itemWzPath, outputPath, WzMapleVersion.CLASSIC);

            //accessoryWz.ParseWzFile();

            try
            {
                
                WzFileParseStatus parseStatus = etcWz.ParseWzFile();

                if (parseStatus == WzFileParseStatus.Success)
                {
                    Console.WriteLine("Successfully parsed Etc.wz");
                    PrintWzStructure(etcWz.WzDirectory, 0);

                    //List<int> itemIds = ExtractCommodityData(etcWz);
                    var itemData = ExtractItemData(etcWz, stringWz, itemWz);
                    var itemIds = itemData.Select(item => item.ItemId).Distinct().ToList();

                    outputPath = "output/CharacterItems";
                    string characterPath = @"WzFiles\Character";

                    string itemOutputPath = "output/Item";
                    string itemPath = @"WzFiles\Item";
                    CharacterWzDumper.DumpCharacterWzData(characterPath, itemIds, outputPath);

                    


                    // Dump Etc.wz and String.wz data
                    //DumpEtcWzData(etcWz);
                    //DumpStringWzData(stringWz);
                    //PrintWzStructure(accessoryWz.WzDirectory, 0);

                    //DumpCharacterWzData(characterWz);
                    //PrintWzStructure(characterWz.WzDirectory, 0);
                    // Dump Item.wz and Character.wz data for each item
                    foreach (var item in itemData)
                    {
                        //DumpItemWzData(itemWz, item.ItemId);

                    }

                    // Save the extracted data
                    string jsonOutput = JsonSerializer.Serialize(itemData, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText("output.json", jsonOutput);

                    Console.WriteLine("Data extraction complete. Check the output files for results.");
                }
                else
                {
                    Console.WriteLine($"Failed to parse Etc.wz. Status: {parseStatus}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
        static void ExtractItemDirectory(string itemDirPath, string outputPath, WzMapleVersion version)
        {
            try
            {
                Console.WriteLine($"Attempting to access Item directory: {itemDirPath}");

                if (!Directory.Exists(itemDirPath))
                {
                    Console.WriteLine($"Item directory not found: {itemDirPath}");
                    return;
                }

                // Try to get a list of all .wz files without using SearchOption.AllDirectories
                List<string> wzFiles = new List<string>();
                foreach (string dir in Directory.GetDirectories(itemDirPath))
                {
                    Console.WriteLine($"Scanning subdirectory: {dir}");
                    try
                    {
                        wzFiles.AddRange(Directory.GetFiles(dir, "*.wz"));
                    }
                    catch (UnauthorizedAccessException uae)
                    {
                        Console.WriteLine($"Cannot access subdirectory {dir}: {uae.Message}");
                    }
                }

                Console.WriteLine($"Found {wzFiles.Count} .wz files in Item directory");

                foreach (string wzFilePath in wzFiles)
                {
                    string relativePath = Path.GetRelativePath(itemDirPath, wzFilePath);
                    string targetDir = Path.Combine(outputPath, "Item", Path.GetDirectoryName(relativePath));

                    Console.WriteLine($"Extracting: {wzFilePath} to {targetDir}");
                    ExtractWzFile(wzFilePath, targetDir, version);
                }

                Console.WriteLine($"Successfully extracted all Item WZ files to {Path.Combine(outputPath, "Item")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting Item directory: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        static void ExtractWzFile(string wzFilePath, string outputPath, WzMapleVersion version)
        {
            WzFile wzFile = null;
            try
            {
                wzFile = new WzFile(wzFilePath, version);
                WzFileParseStatus parseStatus = wzFile.ParseWzFile();

                if (parseStatus != WzFileParseStatus.Success)
                {
                    Console.WriteLine($"Failed to parse {Path.GetFileName(wzFilePath)}. Status: {parseStatus}");
                    return;
                }

                WzPngMp3Serializer serializer = new WzPngMp3Serializer();
                string directoryPath = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(wzFilePath));
                Directory.CreateDirectory(directoryPath);

                serializer.SerializeFile(wzFile, directoryPath);

                Console.WriteLine($"Successfully extracted {Path.GetFileName(wzFilePath)} to {directoryPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting {Path.GetFileName(wzFilePath)}: {ex.Message}");
            }
            finally
            {
                wzFile?.Dispose();
            }
        }


        public static void PrintWzStructure(WzObject wzObject, int depth)
        {
            string indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}{wzObject.Name}");

            if (wzObject is WzDirectory directory)
            {
                foreach (WzImage image in directory.WzImages)
                {
                    Console.WriteLine($"{indent}  {image.Name}");
                }

                foreach (WzDirectory subDir in directory.WzDirectories)
                {
                    PrintWzStructure(subDir, depth + 1);
                }
            }
            else if (wzObject is WzImage image)
            {
                image.ParseImage();
                foreach (WzImageProperty prop in image.WzProperties)
                {
                    PrintWzStructure(prop, depth + 1);
                }
            }
            else if (wzObject is WzImageProperty property)
            {
                if (property is WzSubProperty || property is WzConvexProperty)
                {
                    foreach (WzImageProperty subProp in property.WzProperties)
                    {
                        PrintWzStructure(subProp, depth + 1);
                    }
                }
            }
        }

        static WzImage FindCommodityImg(WzDirectory directory)
        {
            foreach (WzImage image in directory.WzImages)
            {
                if (image.Name.Equals("Commodity.img", StringComparison.OrdinalIgnoreCase))
                {
                    return image;
                }
            }

            foreach (WzDirectory subDir in directory.WzDirectories)
            {
                WzImage result = FindCommodityImg(subDir);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        static List<string> FindFilesContaining(WzDirectory directory, string searchTerm, string currentPath = "")
        {
            List<string> results = new List<string>();

            foreach (WzImage image in directory.WzImages)
            {
                if (image.Name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(Path.Combine(currentPath, image.Name));
                }
            }

            foreach (WzDirectory subDir in directory.WzDirectories)
            {
                string newPath = Path.Combine(currentPath, subDir.Name);
                results.AddRange(FindFilesContaining(subDir, searchTerm, newPath));
            }

            return results;
        }

        static List<int> ExtractCommodityData(WzFile etcWz)
        {
            List<int> itemIds = new List<int>();
            WzImage commodityImg = etcWz.WzDirectory["Commodity.img"] as WzImage;

            if (commodityImg != null)
            {
                Console.WriteLine("Found Commodity.img");
                commodityImg.ParseImage();
                foreach (WzImageProperty prop in commodityImg.WzProperties)
                {
                    WzImageProperty termStartProp = prop["termStart"];
                    WzImageProperty termEndProp = prop["termEnd"];

                    if (termStartProp != null && termEndProp != null)
                    {
                        WzImageProperty itemIdProp = prop["ItemId"];
                        if (itemIdProp != null)
                        {
                            int itemId = itemIdProp.GetInt();
                            itemIds.Add(itemId);

                            Console.WriteLine($"Item ID: {itemId}, Start: {termStartProp.ToString()}, End: {termEndProp.ToString()}");
                        }
                    }
                }
                Console.WriteLine($"Found {itemIds.Count} items");
            }
            else
            {
                Console.WriteLine("Commodity.img not found in Etc.wz");
            }

            return itemIds;
        }

        static List<ItemData> ExtractItemData(WzFile etcWz, WzFile stringWz, WzFile itemWz)
        {
            List<ItemData> itemDataList = new List<ItemData>();
            WzImage commodityImg = etcWz.WzDirectory["Commodity.img"] as WzImage;
            WzImage cashPackageImg = etcWz.WzDirectory["CashPackage.img"] as WzImage;

            if (commodityImg == null || cashPackageImg == null)
            {
                Console.WriteLine("Required images not found in Etc.wz");
                return itemDataList;
            }

            // First, process Commodity.img to get package IDs and items with termStart/termEnd
            Dictionary<int, ItemData> packageItems = new Dictionary<int, ItemData>();
            HashSet<int> relevantSNs = new HashSet<int>();

            foreach (WzImageProperty entry in commodityImg.WzProperties)
            {
                if (entry is WzSubProperty subProp)
                {
                    int itemId = subProp["ItemId"]?.GetInt() ?? 0;
                    int sn = subProp["SN"]?.GetInt() ?? 0;
                    bool hasTerms = subProp["termStart"] != null && subProp["termEnd"] != null;

                    if (itemId.ToString().StartsWith("910") && hasTerms)  // It's a package
                    {
                        packageItems[itemId] = CreateItemData(subProp);
                        relevantSNs.Add(sn);
                    }
                    else if (hasTerms)  // Regular item with termStart and termEnd
                    {
                        itemDataList.Add(CreateItemData(subProp));
                        relevantSNs.Add(sn);
                    }
                }
            }

            // Process CashPackage.img for the packages we found
            foreach (var packageItem in packageItems)
            {
                WzImageProperty packageProp = cashPackageImg[packageItem.Key.ToString()];
                if (packageProp != null && packageProp["SN"] is WzSubProperty snSubProp)
                {
                    foreach (WzImageProperty itemProp in snSubProp.WzProperties)
                    {
                        int contentSN = itemProp.GetInt();
                        relevantSNs.Add(contentSN);
                    }
                }
            }

            // Now process Commodity.img again to create ItemData objects for package contents
            foreach (WzImageProperty entry in commodityImg.WzProperties)
            {
                if (entry is WzSubProperty subProp)
                {
                    int sn = subProp["SN"]?.GetInt() ?? 0;
                    if (relevantSNs.Contains(sn) && !itemDataList.Any(item => item.SN == sn))
                    {
                        ItemData itemData = CreateItemData(subProp);
                        itemData.IsPackageContent = true;

                        // Find which package this item belongs to
                        foreach (var packageItem in packageItems)
                        {
                            WzImageProperty packageProp = cashPackageImg[packageItem.Key.ToString()];
                            if (packageProp != null && packageProp["SN"] is WzSubProperty snSubProp)
                            {
                                if (snSubProp.WzProperties.Any(p => p.GetInt() == sn))
                                {
                                    itemData.PackageId = packageItem.Key;
                                    break;
                                }
                            }
                        }

                        itemDataList.Add(itemData);
                    }
                }
            }

            // Add the package items themselves
            itemDataList.AddRange(packageItems.Values);

            Console.WriteLine($"Total items processed: {itemDataList.Count}");
            return itemDataList;
        }

        static ItemData CreateItemData(WzSubProperty subProp)
        {
            return new ItemData
            {
                ItemId = subProp["ItemId"]?.GetInt() ?? 0,
                SN = subProp["SN"]?.GetInt() ?? 0,
                Count = subProp["Count"]?.GetInt() ?? 0,
                Price = subProp["Price"]?.GetInt() ?? 0,
                Bonus = subProp["Bonus"]?.GetInt() ?? 0,
                Period = subProp["Period"]?.GetInt() ?? 0,
                Priority = subProp["Priority"]?.GetInt() ?? 0,
                ReqPOP = subProp["ReqPOP"]?.GetInt() ?? 0,
                ReqLEV = subProp["ReqLEV"]?.GetInt() ?? 0,
                Gender = subProp["Gender"]?.GetInt() ?? 0,
                OnSale = subProp["OnSale"]?.GetInt() == 1,
                TermStart = subProp["termStart"]?.ToString(),
                TermEnd = subProp["termEnd"]?.ToString(),
                PbCash = subProp["PbCash"]?.GetInt() ?? 0,
                PbPoint = subProp["PbPoint"]?.GetInt() ?? 0,
                PbGift = subProp["PbGift"]?.GetInt() ?? 0,
                Refundable = subProp["Refundable"]?.GetInt() == 1,
                WebShop = subProp["WebShop"]?.GetInt() == 1,
                IsGift = subProp["IsGift"]?.GetInt() == 1,
                GameWorld = subProp["GameWorld"]?.ToString()
            };
        }

        static void ProcessPackageContents(WzImage cashPackageImg, int packageId, ItemData packageData, List<ItemData> itemDataList)
        {
            WzImageProperty packageProp = cashPackageImg[packageId.ToString()];
            if (packageProp != null && packageProp["SN"] is WzSubProperty snProp)
            {
                foreach (WzImageProperty itemProp in snProp.WzProperties)
                {
                    int contentItemId = itemProp.GetInt();
                    ItemData contentItemData = new ItemData
                    {
                        ItemId = contentItemId,
                        SN = packageData.SN,
                        TermStart = packageData.TermStart,
                        TermEnd = packageData.TermEnd,
                        Price = packageData.Price,
                        // Copy other relevant fields from packageData
                        IsPackageContent = true,
                        PackageId = packageId
                    };
                    itemDataList.Add(contentItemData);
                }
            }
        }

        static void DumpEtcWzData(WzFile etcWz)
        {
            WzImage commodityImg = etcWz.WzDirectory["Commodity.img"] as WzImage;
            if (commodityImg != null)
            {
                string jsonOutput = JsonSerializer.Serialize(commodityImg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("Etc_Commodity.json", jsonOutput);
            }
        }

        static void DumpStringWzData(WzFile stringWz)
        {
            WzImage itemImg = stringWz.WzDirectory["Cash.img"] as WzImage;
            if (itemImg != null)
            {
                string jsonOutput = JsonSerializer.Serialize(itemImg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("String_Item.json", jsonOutput);
            }
        }

        static void DumpItemWzData(WzFile itemWz, int itemId)
        {
            string[] categories = { "Consume", "Etc", "Install", "Cash", "Pet" };
            foreach (var category in categories)
            {
                WzImage itemImg = itemWz.GetObjectFromPath($"{category}/{itemId:D8}.img") as WzImage;
                if (itemImg != null)
                {
                    string jsonOutput = JsonSerializer.Serialize(itemImg, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText($"Item_{category}_{itemId}.json", jsonOutput);
                }
            }
        }



        static long DirSize(DirectoryInfo dirInfo)
        {
            long size = 0;
            FileInfo[] fis = dirInfo.GetFiles();
            foreach (FileInfo fi in fis)
            {
                size += fi.Length;
            }
            DirectoryInfo[] dis = dirInfo.GetDirectories();
            foreach (DirectoryInfo di in dis)
            {
                size += DirSize(di);
            }
            return size;
        }

        static string GetItemCategory(int itemId)
        {
            int category = itemId / 1000000;
            switch (category)
            {
                case 1: return "Equip";
                case 2: return "Consume";
                case 3: return "Install";
                case 4: return "Etc";
                case 5: return "Cash";
                default: return "Unknown";
            }
        }

        static List<String> GetWzFilesInFolder(String path)
        {
            List<String> wzFiles = new List<String>();
            string[] dirs = Directory.GetDirectories(path);
            foreach (var dir in dirs)
            {
                wzFiles.AddRange(Directory.GetFiles(dir, "*.wz"));
            }
            return wzFiles;
        }

        class ItemData
        {
            public bool IsPackageContent { get; set; }
            public int? PackageId { get; set; }
            public int ItemId { get; set; }
            public int SN { get; set; }
            // public string Name { get; set; }
            public string TermStart { get; set; }
            public string TermEnd { get; set; }
            public int Count { get; set; }
            public int Price { get; set; }
            public int Bonus { get; set; }
            public int Period { get; set; }
            public int Priority { get; set; }
            public int ReqPOP { get; set; }
            public int ReqLEV { get; set; }
            public int Gender { get; set; }
            public bool OnSale { get; set; }
            public string GameWorld { get; set; }
            public bool Cash { get; set; }
            public int PbCash { get; set; }
            public int PbPoint { get; set; }
            public int PbGift { get; set; }
            public bool Refundable { get; set; }
            public bool WebShop { get; set; }
            public bool IsGift { get; set; }

            // public string ItemType { get; set; }
        }
    }
}