using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CustomItemSpawner
{
    [BepInPlugin("com.Exocet.customitemspawner", "Custom Item Spawner", "1.1.3")]
    public sealed class CustomItemSpawnerPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.Exocet.customitemspawner";
        public const string PLUGIN_NAME = "Custom Item Spawner";
        public const string PLUGIN_VERSION = "1.1.3";

        internal static ConfigEntry<string> CrateDirectoryItem;
        internal static ConfigEntry<KeyCode> SpawnKey;
        internal static ConfigEntry<bool> CustomBallastObject;
        internal static ConfigEntry<int> CustomObjectMass;
        internal static ManualLogSource Log;
        internal static ConfigFile PluginConfig;
        internal static ItemDirectoryEntry[] ItemDirectory;
        internal static Dictionary<string, int> ItemIndices;
        private static bool itemDirectoryCreated;

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

            new Harmony("com.Exocet.customitemspawner").PatchAll();
            StartCoroutine(InitializeItemDirectoryWhenReady());
            Logger.LogInfo("Custom object spawner loaded.");
        }

        private void Update()
        {
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

            if (PrefabsDirectory.instance == null ||
                PrefabsDirectory.instance.directory == null ||
                PrefabsDirectory.instance.directory.Length == 0)
            {
                return;
            }

            ItemDirectory = CreateItemDirectory.Create();
            Log.LogDebug($"Created item directory with {ItemDirectory.Length} entries.");

            List<string> itemNames = new List<string>();
            ItemIndices = new Dictionary<string, int>();
            for (int index = 0; index < ItemDirectory.Length; index++)
            {
                string itemName = ItemDirectory[index].Name;
                if (itemName != null && !ItemIndices.ContainsKey(itemName))
                {
                    itemNames.Add(itemName);
                    ItemIndices.Add(itemName, ItemDirectory[index].Index);
                }
            }

            if (itemNames.Count == 0)
            {
                return;
            }

            itemDirectoryCreated = true;

            string defaultItemName = ItemDirectory.Length > 23
                ? ItemDirectory[23].Name
                : null;
            if (defaultItemName == null || !ItemIndices.ContainsKey(defaultItemName))
            {
                defaultItemName = itemNames[0];
            }

            if (!ItemIndices.ContainsKey(CrateDirectoryItem.Value))
            {
                CrateDirectoryItem.Value = defaultItemName;
            }

            PluginConfig.Remove(new ConfigDefinition("General", "CrateDirectoryItem"));
            CrateDirectoryItem = PluginConfig.Bind(
                "General",
                "Game Item Directory",
                defaultItemName,
                new ConfigDescription(
                    "Game object to spawn.",
                    new AcceptableValueList<string>(itemNames.ToArray())));
        }

        private static IEnumerator InitializeItemDirectoryWhenReady()
        {
            while (!itemDirectoryCreated)
            {
                CreateItemDirectoryOnce();
                yield return null;
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
            if (item == null || !CustomItemSpawnerPlugin.CustomBallastObject.Value)
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
            if (!CustomItemSpawnerPlugin.CustomBallastObject.Value ||
                SaveLoadManager.instance == null)
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
