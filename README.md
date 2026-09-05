# Sailwind Custom Item Spawner
Plugin to allow users to quickly spawn any game item with a single key press. Users can customize the mass of the spawned object to create ballast for ships.

Requires [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager)

### How to Use
Open the BepInEx configuration menu (F1) and select the options you would like under the **CustomItemSpanwer** header.
1. Select the object you would like to spawn from the list of all available game items. This will include any items from mods you currently have loaded when starting the game.
2. Check if you would like to customize the object's mass. If left unchecked the original, unmodified item is spawned.
3. If using a custom mass, set the desired mass using the slider or text entry. Minimum mass is 1,000 with maximum up to 100,000 mass units.
4. Return to the game and press the spawn item key (F6 by default) to spawn your desired item.

### Mod Options 
Rebind the item spawn key to any standard keyboard input. Modifier keys are not allowed. F6 is the default.

### Disclaimers
If you spawn an item that uses a prefab from a mod and then uninstall that mod, the game will likely not load. Recommend using the [Save Cleaner](https://github.com/NANDbrew/SaveCleaner) mod by NANDBrew to remove any spawned objects that would cause errors.

This mod was developed with the use of generative AI using the GPT-5.6 Luna model.
