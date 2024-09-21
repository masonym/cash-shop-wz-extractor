# cash-shop-wz-extractor
A tool that utilizes MapleLib to parse relevant wz files &amp; dump data for upcoming &amp; past cash shop updates

This tool uses MapleLib/WzLib to parse through Commodity.img to gather which items we need to gather info on for the current patches sales.

This is the first thing I've written in and I got pretty lost in the weeds with all of the classes in WzLib, so it is very messy and bad! Maybe one day I'll re-write it.

# Usage

For now, this tool looks for folders of Wz files in the `/WzFiles/` directory, meaning you should copy them straight from your MapleStory installation. It will dump all relevant items for upcoming cash shop sales in `output/CharacterItems/{item category}`. These include xml files for item data, and pngs for item icons/sprites.

This tool is meant for use with my [Cash Shop Parser](https://github.com/masonym/maple-cs-parser) file, and also relies on the usage of [Wz-Dumper](https://github.com/Xterminatorz/WZ-Dumper), because I haven't bothered to write functions to parse Etc.wz, Item.wz, and String.wz yet. 


# Technology used
- [MapleLib](https://github.com/lastbattle/MapleLib) - a very handy library for working with WZ files in C#.
