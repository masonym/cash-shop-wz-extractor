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

namespace WzDataExtractor
{
    public class CharacterWzDumper
    {
        public static void DumpCharacterWzData(string characterWzPath, List<int> itemIds, string outputPath)
        {
            var files = GetWzFilesInFolder(characterWzPath);
            foreach (var file in files)
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
                            DumpItemData(itemImg, itemId, Path.Combine(outputPath, subfolder));
                        }
                    }
                }
            }
        }

        private static List<string> GetWzFilesInFolder(string path)
        {
            List<string> wzFiles = new List<string>();
            wzFiles.AddRange(Directory.GetFiles(path, "*.wz"));
            string[] dirs = Directory.GetDirectories(path);
            foreach (var dir in dirs)
            {
                wzFiles.AddRange(Directory.GetFiles(dir, "*.wz"));
            }
            return wzFiles.Where(f => Regex.IsMatch(Path.GetFileName(f), @"\d{3}\.wz$", RegexOptions.IgnoreCase)).ToList();
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
            int category = itemId / 10000; // Extract category

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

        private static void DumpItemData(WzImage itemImg, int itemId, string outputPath)
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            string xmlPath = Path.Combine(outputPath, $"{itemId:D8}.xml");
            using (StreamWriter sw = new StreamWriter(xmlPath))
            using (XmlWriter xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings { Indent = true }))
            {
                xmlWriter.WriteStartDocument();
                xmlWriter.WriteStartElement("imgdir");
                xmlWriter.WriteAttributeString("name", itemImg.Name);

                foreach (WzImageProperty prop in itemImg.WzProperties)
                {
                    DumpProperty(xmlWriter, prop, outputPath);
                }

                xmlWriter.WriteEndElement();
                xmlWriter.WriteEndDocument();
            }
        }

        private static void DumpProperty(XmlWriter xmlWriter, WzImageProperty prop, string outputPath)
        {
            switch (prop.PropertyType)
            {
                case WzPropertyType.Canvas:
                    DumpCanvasProperty(xmlWriter, (WzCanvasProperty)prop, outputPath);
                    break;
                case WzPropertyType.Vector:
                    DumpVectorProperty(xmlWriter, (WzVectorProperty)prop);
                    break;
                default:
                    xmlWriter.WriteStartElement(prop.PropertyType.ToString().ToLower());
                    xmlWriter.WriteAttributeString("name", prop.Name);
                    xmlWriter.WriteAttributeString("value", prop.ToString());
                    xmlWriter.WriteEndElement();
                    break;
            }
        }

        private static void DumpCanvasProperty(XmlWriter xmlWriter, WzCanvasProperty canvasProp, string outputPath)
        {
            xmlWriter.WriteStartElement("canvas");
            xmlWriter.WriteAttributeString("name", canvasProp.Name);

            WzPngProperty pngProp = canvasProp.PngProperty;
            if (pngProp != null)
            {
                xmlWriter.WriteAttributeString("width", pngProp.Width.ToString());
                xmlWriter.WriteAttributeString("height", pngProp.Height.ToString());

                string pngFileName = $"{canvasProp.Name}.png";
                string pngPath = Path.Combine(outputPath, pngFileName);

                // Check for _inlink or _outlink
                WzImageProperty linkProp = canvasProp.GetLinkedWzImageProperty();
                if (linkProp != canvasProp)
                {
                    // Handle linked property
                    if (linkProp is WzCanvasProperty linkedCanvas)
                    {
                        pngProp = linkedCanvas.PngProperty;
                    }
                    else if (linkProp is WzPngProperty linkedPng)
                    {
                        pngProp = linkedPng;
                    }

                    xmlWriter.WriteAttributeString("link", linkProp.FullPath);
                }

                // Save PNG
                using (Bitmap bmp = pngProp.GetImage(false))
                {
                    if (bmp != null)
                    {
                        bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                        xmlWriter.WriteAttributeString("png", pngFileName);
                    }
                }
            }

            // Dump sub-properties
            foreach (WzImageProperty subProp in canvasProp.WzProperties)
            {
                DumpProperties(xmlWriter, subProp, outputPath);
            }

            xmlWriter.WriteEndElement(); // canvas
        }

        private static void DumpProperties(XmlWriter xmlWriter, WzImageProperty prop, string outputPath)
        {
            switch (prop.PropertyType)
            {
                case WzPropertyType.Canvas:
                    DumpCanvasProperty(xmlWriter, (WzCanvasProperty)prop, outputPath);
                    break;
                case WzPropertyType.Vector:
                    DumpVectorProperty(xmlWriter, (WzVectorProperty)prop);
                    break;
                // Add cases for other property types as needed
                default:
                    xmlWriter.WriteStartElement(prop.PropertyType.ToString().ToLower());
                    xmlWriter.WriteAttributeString("name", prop.Name);
                    xmlWriter.WriteAttributeString("value", prop.ToString());
                    xmlWriter.WriteEndElement();
                    break;
            }
        }

        private static void DumpVectorProperty(XmlWriter xmlWriter, WzVectorProperty vectorProp)
        {
            xmlWriter.WriteStartElement("vector");
            xmlWriter.WriteAttributeString("name", vectorProp.Name);
            xmlWriter.WriteAttributeString("x", vectorProp.X.Value.ToString());
            xmlWriter.WriteAttributeString("y", vectorProp.Y.Value.ToString());
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

        static void PrintWzStructure(WzObject wzObject, int depth)
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

        static void DumpCharacterWzData(WzFile characterWz)
        {
            string filesFound = "";
            string filePath = @"WzFiles\Character";
            var files = GetWzFilesInFolder(filePath);
            var allFiles = files.ToArray();
            allFiles = allFiles.Where(fileName => Regex.IsMatch(fileName, "[0-9]{3}.wz$", RegexOptions.IgnoreCase)).ToArray();
            Console.WriteLine(allFiles.Length);

            foreach (var fileName in allFiles)
            {
                filesFound += Path.GetFileName(fileName) + ", ";
            }
            SortedDictionary<long, string> fileOrder = new SortedDictionary<long, string>();
            foreach (var fileName in allFiles)
            {
                FileInfo wzFile = new FileInfo(fileName);
                fileOrder.Add(DirSize(wzFile.Directory), fileName);
            }
            allFiles = fileOrder.Values.ToArray();

            Console.WriteLine(filesFound);
            Console.WriteLine(String.Join("\n", allFiles));

            foreach (var file in allFiles)
            {
                ExportFile(file, "/output");
            }

            /*
            string[] categories = { "Weapon", "Pants", "Coat", "Longcoat", "Shoes", "Glove", "Cap", "Cape", "Shield", "Accessory", "Medal", "Ring" };
            foreach (var category in categories)
            {
                //Console.WriteLine($"Checking category: {category}/{itemId:D8}.img");
                // WzObject itemImg = characterWz.GetObjectFromPath($"{category}/{itemId:D8}.img");
                //WzObject itemImg = characterWz.WzDirectory[$"{category}/{itemId:D8}.img"];
                //Console.WriteLine(characterWz.WzDirectory.);
                //PrintWzStructure(characterWz.WzDirectory, 0);
                //Console.WriteLine($"Item image: {itemImg}");
                if (itemImg != null)
                {
                    string jsonOutput = JsonSerializer.Serialize(itemImg, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText($"Character_{category}_{itemId}.json", jsonOutput);
                }
            }
            */
        }

        static void ExportFile(string fileName, string outputFolder)
        {
            WzFile regFile = null;
            regFile = new WzFile(fileName, WzMapleVersion.CLASSIC);
            regFile.ParseWzFile();
            Directory.CreateDirectory(@"output/");

            // we want to export each `Character\{category}\{itemID}.img\` file for the given item ids

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