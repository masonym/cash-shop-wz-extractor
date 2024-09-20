using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MapleLib.WzLib;
using MapleLib.WzLib.WzProperties;
using MapleLib.WzLib.WzStructure.Data.ItemStructure;

namespace WzDataExtractor
{
    class Program
    {
        static void Main(string[] args)
        {
            string etcWzPath = @"WzFiles\Etc\Etc_000.wz";
            string characterWzPath = @"WzFiles\Character\Character.wz";
            string stringWzPath = @"WzFiles\String\String.wz";
            string itemWzPath = @"WzFiles\Item\Item.wz";

            WzFile characterWz = new WzFile(characterWzPath, WzMapleVersion.CLASSIC);
            WzFile stringWz = new WzFile(stringWzPath, WzMapleVersion.CLASSIC);
            WzFile itemWz = new WzFile(itemWzPath, WzMapleVersion.CLASSIC);

            characterWz.ParseWzFile();
            stringWz.ParseWzFile();
            itemWz.ParseWzFile();

            try
            {
                WzFile etcWz = new WzFile(etcWzPath, WzMapleVersion.CLASSIC);
                WzFileParseStatus parseStatus = etcWz.ParseWzFile();

                if (parseStatus == WzFileParseStatus.Success)
                {
                    Console.WriteLine("Successfully parsed Etc.wz");
                    PrintWzStructure(etcWz.WzDirectory, 0);

                    Console.WriteLine("\nSearching for Commodity.img...");
                    WzImage commodityImg = etcWz.WzDirectory["Commodity.img"] as WzImage;

                    if (commodityImg != null)
                    {
                        List<int> itemIds = ExtractCommodityData(etcWz);
                        Console.WriteLine($"Found {itemIds.Count} items in Commodity.img");
                        List<ItemData> itemDataList = ExtractItemData(itemIds, characterWz, stringWz, itemWz);
                        Console.WriteLine("Commodity.img found. Extracting data...");

                        string jsonOutput = JsonSerializer.Serialize(itemDataList, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText("output.json", jsonOutput);

                        Console.WriteLine("Data extraction complete. Check output.json for results.");
                    }
                    else
                    {
                        Console.WriteLine("Commodity.img not found in Etc.wz");
                        Console.WriteLine("Searching for files with 'Commodity' in the name...");
                        List<string> commodityFiles = FindFilesContaining(etcWz.WzDirectory, "Commodity");
                        foreach (string file in commodityFiles)
                        {
                            Console.WriteLine($"Found: {file}");
                        }
                    }
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
                            Console.WriteLine($"Item ID: {itemId}, Start: {termStartProp.GetString()}, End: {termEndProp.GetString()}");
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

        static List<ItemData> ExtractItemData(List<int> itemIds, WzFile characterWz, WzFile stringWz, WzFile itemWz)
        {
            List<ItemData> itemDataList = new List<ItemData>();
            Console.WriteLine(itemDataList);
            foreach (int itemId in itemIds)
            {
                ItemData itemData = new ItemData { ItemId = itemId };
                Console.WriteLine(itemId);

                // Extract data from Character.wz
                WzImage characterImg = characterWz.GetObjectFromPath($"Weapon/{itemId:D8}.img") as WzImage;
                if (characterImg != null)
                {
                    characterImg.ParseImage();
                    // Extract relevant data from characterImg
                    // Example: itemData.SomeProperty = characterImg["info"]["someValue"].GetInt();
                }

                // Extract data from String.wz
                WzImage stringImg = stringWz.GetObjectFromPath("Item.img") as WzImage;
                if (stringImg != null)
                {
                    stringImg.ParseImage();
                    WzImageProperty itemNameProp = stringImg[$"name/{itemId}"];
                    itemData.Name = itemNameProp?.GetString();
                }

                // Extract data from Item.wz
                WzImage itemImg = itemWz.GetObjectFromPath($"Weapon/{itemId:D8}.img") as WzImage;
                if (itemImg != null)
                {
                    itemImg.ParseImage();
                    // Extract relevant data from itemImg
                    // Example: itemData.AnotherProperty = itemImg["info"]["anotherValue"].GetInt();
                }

                itemDataList.Add(itemData);
            }

            return itemDataList;
        }

        class ItemData
        {
            public int ItemId { get; set; }
            public string Name { get; set; }
            // Add other properties as needed
        }
    }
}