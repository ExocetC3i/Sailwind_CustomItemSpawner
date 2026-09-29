using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace CustomItemSpawner
{
    [BepInPlugin("com.Exocet.customitemspawner", "Custom Item Spawner", "1.2.0")]
    public sealed class CustomItemSpawnerPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.Exocet.customitemspawner";
        public const string PLUGIN_NAME = "Custom Item Spawner";
        public const string PLUGIN_VERSION = "1.2.0";

        internal static ConfigEntry<string> CrateDirectoryItem;
        internal static ConfigEntry<string> ItemKeywordSearch;
        internal static ConfigEntry<bool> ClearItemKeywordSearch;
        internal static ConfigEntry<KeyCode> SpawnKey;
        internal static ConfigEntry<bool> CustomBallastObject;
        internal static ConfigEntry<int> CustomObjectMass;
        internal static ManualLogSource Log;
        internal static ConfigFile PluginConfig;
        internal static ItemDirectoryEntry[] ItemDirectory;
        internal static Dictionary<string, int> ItemIndices;
        private static bool itemDirectoryCreated;
        private static bool configurationManagerRefreshRequested;

        private void Awake()
        {
            Log = Logger;
            PluginConfig = Config;

            CrateDirectoryItem = Config.Bind(
                "General",
                "CrateDirectoryItem",
                "(Loading item directory...)",
                new ConfigDescription(
                    "Prefab name from PrefabsDirectory.directory used as the custom object.",
                    new AcceptableValueList<string>("(Loading item directory...)")));

            ItemKeywordSearch = Config.Bind(
                "General",
                "Item Keyword Search",
                string.Empty,
                "Filter the game item directory by a case-insensitive keyword.");

            ClearItemKeywordSearch = Config.Bind(
                "General",
                "Clear Item Keyword Search",
                false,
                "Set to true to clear the keyword search and show the full game item list. This button resets itself after use.");

            CustomBallastObject = Config.Bind(
                "General",
                "CustomBallastObject",
                false,
                "Apply the custom mass and name to spawned objects.");

            CustomObjectMass = Config.Bind(
                "General",
                "CustomObjectMass",
                10000,
                new ConfigDescription(
                    "User defined mass value for this spawned object.",
                    new AcceptableValueRange<int>(1, 100000)));

            SpawnKey = Config.Bind(
                "General",
                "SpawnKey",
                KeyCode.F6,
                "Key used to spawn the custom object.");

            ItemKeywordSearch.SettingChanged += (_, __) => UpdateGameItemDropdown();
            ClearItemKeywordSearch.SettingChanged += (_, __) => ClearItemKeywordSearchIfRequested();

            new Harmony("com.Exocet.customitemspawner").PatchAll();
            StartCoroutine(InitializeItemDirectoryWhenReady());
            Logger.LogInfo("Custom object spawner loaded.");
        }

        private void Update()
        {
            if (configurationManagerRefreshRequested)
            {
                RefreshConfigurationManagerSettings();
            }

            if (Input.GetKeyDown(SpawnKey.Value))
            {
                CustomItemSpawner.Spawn();
            }
        }

        internal static void CreateItemDirectoryOnce()
        {
            if (itemDirectoryCreated)
            {
                return;
            }

            RebuildItemDirectory();
        }

        private static void RebuildItemDirectory()
        {
            if (PrefabsDirectory.instance == null ||
                PrefabsDirectory.instance.directory == null ||
                PrefabsDirectory.instance.directory.Length == 0)
            {
                return;
            }

            ItemDirectoryEntry[] rebuiltDirectory = CreateItemDirectory.Create();

            List<string> itemNames = new List<string>();
            Dictionary<string, int> rebuiltIndices = new Dictionary<string, int>();
            for (int index = 0; index < rebuiltDirectory.Length; index++)
            {
                string itemName = rebuiltDirectory[index].Name;
                if (itemName != null && !rebuiltIndices.ContainsKey(itemName))
                {
                    itemNames.Add(itemName);
                    rebuiltIndices.Add(itemName, rebuiltDirectory[index].Index);
                }
            }

            if (itemNames.Count == 0)
            {
                return;
            }

            ItemDirectory = rebuiltDirectory;
            ItemIndices = rebuiltIndices;

            string defaultItemName = rebuiltDirectory.Length > 23
                ? rebuiltDirectory[23].Name
                : null;
            if (defaultItemName == null || !ItemIndices.ContainsKey(defaultItemName))
            {
                defaultItemName = itemNames[0];
            }

            itemDirectoryCreated = true;
            UpdateGameItemDropdown(defaultItemName);
            Log.LogDebug($"Rebuilt item directory with {ItemDirectory.Length} entries.");
        }

        private static void UpdateGameItemDropdown(string preferredSelection = null)
        {
            if (!itemDirectoryCreated)
            {
                return;
            }

            List<string> availableItems = new List<string>();
            string keyword = ItemKeywordSearch.Value == null
                ? string.Empty
                : ItemKeywordSearch.Value.Trim();
            for (int index = 0; index < ItemDirectory.Length; index++)
            {
                string itemName = ItemDirectory[index].Name;
                if (itemName != null &&
                    ItemIndices[itemName] == ItemDirectory[index].Index &&
                    (keyword.Length == 0 ||
                        itemName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    availableItems.Add(itemName);
                }
            }

            const string noMatches = "(No matching game items)";
            if (availableItems.Count == 0)
            {
                availableItems.Add(noMatches);
            }

            string currentSelection = CrateDirectoryItem == null
                ? null
                : CrateDirectoryItem.Value;
            string selection = availableItems.Contains(currentSelection)
                ? currentSelection
                : availableItems.Contains(preferredSelection)
                    ? preferredSelection
                    : availableItems[0];

            PluginConfig.Remove(new ConfigDefinition("General", "CrateDirectoryItem"));
            PluginConfig.Remove(new ConfigDefinition("General", "Game Item Directory"));
            CrateDirectoryItem = PluginConfig.Bind(
                "General",
                "Game Item Directory",
                selection,
                new ConfigDescription(
                    "Game object to spawn.",
                    new AcceptableValueList<string>(availableItems.ToArray())));

            if (CrateDirectoryItem.Value != selection)
            {
                CrateDirectoryItem.Value = selection;
            }

            configurationManagerRefreshRequested = true;
        }

        private static void RefreshConfigurationManagerSettings()
        {
            configurationManagerRefreshRequested = false;
            if (!Chainloader.PluginInfos.TryGetValue(
                "com.bepis.bepinex.configurationmanager",
                out BepInEx.PluginInfo configurationManagerInfo) ||
                configurationManagerInfo.Instance == null)
            {
                return;
            }

            MethodInfo rebuildSettings = configurationManagerInfo.Instance.GetType().GetMethod(
                "BuildSettingList",
                BindingFlags.Instance | BindingFlags.Public);
            if (rebuildSettings == null)
            {
                Log.LogWarning(
                    "Configuration Manager was found but does not expose BuildSettingList; " +
                    "reopen its window to refresh the game item dropdown.");
                return;
            }

            rebuildSettings.Invoke(configurationManagerInfo.Instance, null);
            Log.LogDebug("Refreshed Configuration Manager settings after updating the game item dropdown.");
        }

        private static void ClearItemKeywordSearchIfRequested()
        {
            if (!ClearItemKeywordSearch.Value)
            {
                return;
            }

            ItemKeywordSearch.Value = string.Empty;
            ClearItemKeywordSearch.Value = false;
        }

        private static IEnumerator InitializeItemDirectoryWhenReady()
        {
            while (!itemDirectoryCreated)
            {
                CreateItemDirectoryOnce();
                yield return null;
            }

            if (ClearItemKeywordSearch.Value)
            {
                ClearItemKeywordSearchIfRequested();
            }
        }
    }

    internal static class CustomItemSpawner
    {
        private const string ModDataPrefix = "CustomItemSpawner.item.";
        private const string MarkerSuffix = ".customObject";
        private const string MassSuffix = ".mass";
        private const string NameSuffix = ".name";

        internal static void Spawn()
        {
            PrefabsDirectory directory = SaveLoadManager.instance == null
                ? null
                : SaveLoadManager.instance.GetComponent<PrefabsDirectory>();

            string selectedItem = CustomItemSpawnerPlugin.CrateDirectoryItem == null
                ? null
                : CustomItemSpawnerPlugin.CrateDirectoryItem.Value;
            int index = CustomItemSpawnerPlugin.ItemIndices == null ||
                selectedItem == null ||
                !CustomItemSpawnerPlugin.ItemIndices.TryGetValue(selectedItem, out int selectedIndex)
                ? -1
                : selectedIndex;
            if (directory == null || directory.directory == null ||
                index < 0 || index >= directory.directory.Length)
            {
                CustomItemSpawnerPlugin
                    .Log.LogError($"Cannot spawn custom object: prefab '{selectedItem}' is unavailable.");
                return;
            }

            GameObject prefab = directory.directory[index];
            if (prefab == null)
            {
                CustomItemSpawnerPlugin.Log.LogError(
                    $"Cannot spawn custom object: PrefabsDirectory index {index} is empty.");
                return;
            }

            if (Refs.ovrCameraRig == null)
            {
                CustomItemSpawnerPlugin.Log.LogError(
                    "Cannot spawn custom object: the camera rig is unavailable.");
                return;
            }

            GameObject crate = Object.Instantiate(
                prefab,
                Refs.ovrCameraRig.position + Refs.ovrCameraRig.forward,
                Refs.ovrCameraRig.rotation);

            ShipItem item = crate.GetComponent<ShipItem>();
            SaveablePrefab saveable = crate.GetComponent<SaveablePrefab>();
            Good good = crate.GetComponent<Good>();
            if (item == null || saveable == null)
            {
                Object.Destroy(crate);
                CustomItemSpawnerPlugin.Log.LogError(
                    "Cannot spawn custom object: the selected prefab is missing required components.");
                return;
            }

            item.sold = true;
            saveable.RegisterToSave();
            if (CustomItemSpawnerPlugin.CustomBallastObject.Value)
            {
                int customObjectMass = CustomItemSpawnerPlugin.CustomObjectMass.Value;
                item.mass = customObjectMass;
                item.name = $"{customObjectMass} Mass Custom Object";
                SaveCustomObjectProperties(saveable.instanceId, item);
            }
            if (good != null)
            {
                good.RegisterAsMissionless();
            }
            RestoreLoadedCustomObject(item);

            CustomItemSpawnerPlugin.Log.LogInfo(
                $"Spawned custom object '{prefab.name}' from PrefabsDirectory index {index}.");
        }

        internal static void RestoreLoadedCustomObject(ShipItem item)
        {
            if (item == null)
            {
                return;
            }

            SaveablePrefab saveable = item.GetComponent<SaveablePrefab>();
            if (saveable == null || !IsCustomObject(saveable.instanceId))
            {
                return;
            }

            string key = GetModDataKey(saveable.instanceId);
            int mass;
            if (int.TryParse(
                GetModDataValue(key + MassSuffix, ((int)item.mass).ToString(CultureInfo.InvariantCulture)),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out mass))
            {
                item.mass = mass;
            }

            item.name = GetModDataValue(key + NameSuffix, item.name);
        }

        internal static void RestoreLoadedCustomObjects()
        {
            if (SaveLoadManager.instance == null)
            {
                return;
            }

            List<SaveablePrefab> prefabs = SaveLoadManager.instance.GetCurrentPrefabs();
            if (prefabs == null)
            {
                return;
            }

            foreach (SaveablePrefab prefab in prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                RestoreLoadedCustomObject(prefab.GetComponent<ShipItem>());
            }
        }

        internal static IEnumerator RestoreLoadedCustomObjectsAfterLoad()
        {
            for (int frame = 0; frame < 2; frame++)
            {
                yield return null;
                RestoreLoadedCustomObjects();
            }
        }

        private static void SaveCustomObjectProperties(int instanceId, ShipItem item)
        {
            if (GameState.modData == null)
            {
                CustomItemSpawnerPlugin.Log.LogWarning(
                    "Custom object spawned, but save data is unavailable; custom properties cannot persist.");
                return;
            }

            string key = GetModDataKey(instanceId);
            GameState.modData[key + MarkerSuffix] = "1";
            GameState.modData[key + MassSuffix] = item.mass.ToString(CultureInfo.InvariantCulture);
            GameState.modData[key + NameSuffix] = item.name;
        }

        private static bool IsCustomObject(int instanceId)
        {
            return GameState.modData != null &&
                GameState.modData.ContainsKey(GetModDataKey(instanceId) + MarkerSuffix);
        }

        private static string GetModDataKey(int instanceId)
        {
            return ModDataPrefix + instanceId;
        }

        private static string GetModDataValue(string key, string fallback)
        {
            string value;
            return GameState.modData.TryGetValue(key, out value) ? value : fallback;
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadModData))]
    internal static class CustomObjectLoadPatch
    {
        private static void Postfix()
        {
            CustomItemSpawnerPlugin.CreateItemDirectoryOnce();
            CustomItemSpawner.RestoreLoadedCustomObjects();
            if (SaveLoadManager.instance != null)
            {
                SaveLoadManager.instance.StartCoroutine(
                    CustomItemSpawner.RestoreLoadedCustomObjectsAfterLoad());
            }
            CustomItemSpawnerPlugin.Log.LogDebug(
                "Save data loaded; item directory initialized and custom object properties restored.");
        }
    }

    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.OnLoad))]
    internal static class CustomObjectShipItemLoadPatch
    {
        private static void Postfix(ShipItem __instance)
        {
            CustomItemSpawner.RestoreLoadedCustomObject(__instance);
        }
    }

    [HarmonyPatch(typeof(ShipItem), "ProcessSaveable")]
    internal static class CustomObjectProcessSaveablePatch
    {
        private static void Postfix(ShipItem __instance)
        {
            CustomItemSpawner.RestoreLoadedCustomObject(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemRigidbody), nameof(ItemRigidbody.UpdateMass))]
    internal static class CustomObjectMassUpdatePatch
    {
        private static void Prefix(ItemRigidbody __instance)
        {
            if (__instance != null)
            {
                CustomItemSpawner.RestoreLoadedCustomObject(__instance.GetShipItem());
            }
        }
    }

}
