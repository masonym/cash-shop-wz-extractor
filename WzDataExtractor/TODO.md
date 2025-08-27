This project is a mess. I threw it together in a few days.

Currently the workflow is as follows:

0. New maplestory patch comes out 
1. Download the new patch
2. Run this program
3. Move `/Etc`, `/Item`, and `/String` wz file directories to `/{version} wz files` directory
6. Use WzDumper to dump `/{version} wz files` directory to `/dumped_wz`
7. Run `/item-into-generator/python main.py` to generate the item data and push to AWS

I would like the workflow to be:

0. New maplestory patch comes out
1. Download the new patch
2. Run this program; it should ask the user where the WZ files are located
3. Ideally it extracts the CharacterFiles (maybe to maple-cs-parser) and then also extracts necessary Etc, Item, and String files to a new directory
3a. It should also ask the user where to put the dumped WZ files
3b. Necessary files from Etc, Item, and String are:




-- Some easy wins:

1. I think we can just dump Commodity.img and CashPackage.img to XML easily
2. We can just dump Special/0910.img to XML easily
