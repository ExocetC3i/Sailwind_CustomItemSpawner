using UnityEngine;

namespace CustomItemSpawner
{
    internal struct ItemDirectoryEntry
    {
        internal int Index;
        internal string Name;
    }

    internal static class CreateItemDirectory
    {
        internal static ItemDirectoryEntry[] Create()
        {
            GameObject[] prefabs = PrefabsDirectory.instance == null
                ? null
                : PrefabsDirectory.instance.directory;

            if (prefabs == null)
            {
                return new ItemDirectoryEntry[0];
            }

            ItemDirectoryEntry[] itemDirectory = new ItemDirectoryEntry[prefabs.Length];
            for (int index = 0; index < prefabs.Length; index++)
            {
                GameObject item = prefabs[index];
                itemDirectory[index] = new ItemDirectoryEntry
                {
                    Index = index,
                    Name = item == null ? null : item.name
                };
            }

            return itemDirectory;
        }
    }
}
