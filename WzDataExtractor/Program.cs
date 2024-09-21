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
                PrintWzFileContents(canvasWzFile);
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

            string xmlPath = Path.Combine(outputPath, $"{itemId:D8}.xml");
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
            Console.WriteLine($"File name: {fileName}");
            string pngRelativePath = Path.Combine(currentPath, canvasProp.Name + ".png");
            string pngFullPath = Path.Combine(outputPath, pngRelativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(pngFullPath));

            WzImage canvasImage = canvasManager.GetCanvasImage(category, fileName);
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
            string stringWzPath = @"WzFiles\String\String.wz";
            string itemWzPath = @"WzFiles\Item\Item.wz";

            WzFile characterWz = new WzFile(characterWzPath, WzMapleVersion.CLASSIC);
            WzFile stringWz = new WzFile(stringWzPath, WzMapleVersion.CLASSIC);
            WzFile itemWz = new WzFile(itemWzPath, WzMapleVersion.CLASSIC);

            //WzFile accessoryWz = new WzFile(accessoryWzPath, WzMapleVersion.CLASSIC);

            characterWz.ParseWzFile();
            stringWz.ParseWzFile();
            itemWz.ParseWzFile();

            //accessoryWz.ParseWzFile();

            try
            {
                WzFile etcWz = new WzFile(etcWzPath, WzMapleVersion.CLASSIC);
                WzFileParseStatus parseStatus = etcWz.ParseWzFile();

                if (parseStatus == WzFileParseStatus.Success)
                {
                    Console.WriteLine("Successfully parsed Etc.wz");
                    PrintWzStructure(etcWz.WzDirectory, 0);

                    List<int> itemIds = ExtractCommodityData(etcWz);
                    var itemData = ExtractItemData(itemIds, characterWz, stringWz, itemWz, etcWz);

                    string outputPath = "output/CharacterItems";
                    string characterPath = @"WzFiles\Character";
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
                    // Check if both termStart and termEnd exist
                    WzImageProperty termStartProp = prop["termStart"];
                    WzImageProperty termEndProp = prop["termEnd"];

                    if (termStartProp != null && termEndProp != null)
                    {
                        WzImageProperty itemIdProp = prop["itemId"];
                        if (itemIdProp != null)
                        {
                            int itemId = itemIdProp.GetInt();
                            itemIds.Add(itemId);

                            // Optionally, print out the term dates for verification
                            Console.WriteLine($"Item ID: {itemId}, Start: {termStartProp.ToString()}, End: {termEndProp.ToString()}");
                        }
                    }
                }
                Console.WriteLine($"Found {itemIds.Count} items with termStart and termEnd");
            }
            else
            {
                Console.WriteLine("Commodity.img not found in Etc.wz");
            }


            return itemIds;
        }

        static List<ItemData> ExtractItemData(List<int> itemIds, WzFile characterWz, WzFile stringWz, WzFile itemWz, WzFile etcWz)
        {
            List<ItemData> itemDataList = new List<ItemData>();
            WzImage commodityImg = etcWz.WzDirectory["Commodity.img"] as WzImage;
            WzImage stringItemImg = stringWz.WzDirectory["Item.img"] as WzImage;

            Console.WriteLine("Debugging: Processing items with termStart and termEnd");

            if (commodityImg != null)
            {
                foreach (WzImageProperty entry in commodityImg.WzProperties)
                {
                    if (entry is WzSubProperty subProp)
                    {
                        // Check if both termStart and termEnd exist
                        if (subProp["termStart"] != null && subProp["termEnd"] != null)
                        {
                            int itemId = subProp["ItemId"]?.GetInt() ?? 0;
                            if (itemIds.Contains(itemId))
                            {
                                Console.WriteLine($"Processing Item ID: {itemId}");
                                ItemData itemData = new ItemData { ItemId = itemId };

                                itemData.SN = subProp["SN"]?.GetInt() ?? 0;
                                itemData.Count = subProp["Count"]?.GetInt() ?? 0;
                                itemData.Price = subProp["Price"]?.GetInt() ?? 0;
                                itemData.Bonus = subProp["Bonus"]?.GetInt() ?? 0;
                                itemData.Period = subProp["Period"]?.GetInt() ?? 0;
                                itemData.Priority = subProp["Priority"]?.GetInt() ?? 0;
                                itemData.ReqPOP = subProp["ReqPOP"]?.GetInt() ?? 0;
                                itemData.ReqLEV = subProp["ReqLEV"]?.GetInt() ?? 0;
                                itemData.Gender = subProp["Gender"]?.GetInt() ?? 0;
                                itemData.OnSale = subProp["OnSale"]?.GetInt() == 1;
                                itemData.TermStart = subProp["termStart"]?.ToString();
                                itemData.TermEnd = subProp["termEnd"]?.ToString();
                                itemData.PbCash = subProp["PbCash"]?.GetInt() ?? 0;
                                itemData.PbPoint = subProp["PbPoint"]?.GetInt() ?? 0;
                                itemData.PbGift = subProp["PbGift"]?.GetInt() ?? 0;
                                itemData.Refundable = subProp["Refundable"]?.GetInt() == 1;
                                itemData.WebShop = subProp["WebShop"]?.GetInt() == 1;
                                itemData.IsGift = subProp["IsGift"]?.GetInt() == 1;
                                itemData.GameWorld = subProp["GameWorld"]?.ToString();

                                itemDataList.Add(itemData);
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("Commodity.img not found in Etc.wz");
            }

            Console.WriteLine($"Total items processed: {itemDataList.Count}");
            return itemDataList;
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