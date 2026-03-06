using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bro.ReferenceRadar
{
    [Serializable]
    public class CacheData
    {
        [SerializeField] private List<AssetEntry> _entries = new();

        public List<AssetEntry> Entries => _entries;

        public void AddEntry(AssetEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _entries.Add(entry);
        }

        public void RemoveEntry(AssetEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _entries.Remove(entry);
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
