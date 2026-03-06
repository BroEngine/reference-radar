using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bro.ReferenceRadar
{
    [Serializable]
    public class AssetEntry
    {
        [SerializeField] private string _guid;
        [SerializeField] private List<string> _useGuids = new();
        [SerializeField] private AssetType _type;
        [SerializeField] private long _fileTimestamp;

        [NonSerialized] private HashSet<string> _useGuidsSet = new();
        [NonSerialized] private Dictionary<string, AssetEntry> _usedByMap = new();

        public string Guid => _guid;
        public List<string> UseGuids => _useGuids;
        public Dictionary<string, AssetEntry> UsedByMap => _usedByMap;

        public AssetType Type { get => _type; set => _type = value; }
        public long FileTimestamp { get => _fileTimestamp; set => _fileTimestamp = value; }

        public AssetEntry(string guid)
        {
            _guid = guid;
        }

        public void ClearUseGuids()
        {
            _useGuids.Clear();
            _useGuidsSet.Clear();
        }

        public void AddUseGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            // Skip self-references
            if (guid == _guid)
            {
                return;
            }

            if (_useGuidsSet.Add(guid))
            {
                _useGuids.Add(guid);
            }
        }

        // Rebuild HashSet from deserialized List (call after Load)
        public void RebuildUseGuidsSet()
        {
            _useGuidsSet = new HashSet<string>(_useGuids);
        }

        public void InitializeUsedByMap()
        {
            _usedByMap = new Dictionary<string, AssetEntry>();
        }

        public void AddUsedBy(AssetEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _usedByMap[entry.Guid] = entry;
        }
    }
}
