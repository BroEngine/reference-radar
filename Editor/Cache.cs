using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bro.ReferenceRadar
{
    public class Cache
    {
        private const string DefaultCachePath = "Library/ReferenceRadarCache.bin";
        private const uint BinaryMagic = 0x52524430; // "RRD0"
        private const int BinaryVersion = 1;

        private CacheData _data = new();
        private readonly Dictionary<string, AssetEntry> _assetMap = new();
        private readonly Dictionary<string, string> _pathToGuidMap = new();

        public IReadOnlyDictionary<string, AssetEntry> AssetMap => _assetMap;
        public IReadOnlyList<AssetEntry> AssetList => _data.Entries;

        public static string CachePath => Settings.Instance?.CachePath ?? DefaultCachePath;

        public AssetEntry GetEntry(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            return _assetMap.TryGetValue(guid, out var entry) ? entry : null;
        }

        public string GetGuidByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return _pathToGuidMap.TryGetValue(path, out var guid) ? guid : null;
        }

        public AssetEntry AddEntry(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            if (_assetMap.TryGetValue(guid, out var existing))
            {
                return existing;
            }
            var entry = new AssetEntry(guid);
            entry.InitializeUsedByMap();
            _data.AddEntry(entry);
            _assetMap[guid] = entry;
            return entry;
        }

        public void RemoveEntry(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            if (!_assetMap.TryGetValue(guid, out var entry))
            {
                return;
            }
            _data.RemoveEntry(entry);
            _assetMap.Remove(guid);
        }

        public bool Save()
        {
            var path = CachePath;

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    SaveBinary(path);
                }
                else
                {
                    SaveJson(path);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReferenceRadar] failed to save cache to {path}: {e.Message}");
                return false;
            }
        }

        public bool Load()
        {
            var path = CachePath;

            // Auto-migration: if binary path configured but only JSON exists
            if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
            {
                var jsonPath = Path.ChangeExtension(path, ".json");
                if (File.Exists(jsonPath) && LoadFromJson(jsonPath))
                {
                    Debug.Log("[ReferenceRadar] migrated cache from JSON to binary");
                    SaveBinary(path);
                    return true;
                }
            }
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ReferenceRadar] cache file not found at {path}");
                return false;
            }

            try
            {
                if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    LoadBinary(path);
                }
                else
                {
                    return LoadFromJson(path);
                }
                BuildAssetMap();
                return true;
            }
            catch (Exception e)
            {
                _data = new CacheData();
                Debug.LogError($"[ReferenceRadar] failed to load cache from {path}: {e.Message}");
                return false;
            }
        }

        public int ScanProject(bool isForce)
        {
            var allPaths = AssetDatabase.GetAllAssetPaths();

            var settings = Settings.Instance;
            var isScanPackages = settings == null || settings.IsScanPackages;
            var isScanProjectSettings = settings == null || settings.IsScanProjectSettings;

            var validPaths = new List<string>();
            foreach (var path in allPaths)
            {
                var isAssetsPath = path.StartsWith("Assets/", StringComparison.Ordinal);
                var isPackagesPath = isScanPackages && path.StartsWith("Packages/", StringComparison.Ordinal);
                var isProjectSettingsPath = isScanProjectSettings && path.StartsWith("ProjectSettings/", StringComparison.Ordinal);
                if (isAssetsPath || isPackagesPath || isProjectSettingsPath)
                {
                    validPaths.Add(path);
                }
            }

            var activeGuids = new HashSet<string>();
            var scannedCount = 0;

            for (var i = 0; i < validPaths.Count; i++)
            {
                var path = validPaths[i];

                var progress = (float)(i + 1) / validPaths.Count;
                var isCancelled = EditorUtility.DisplayCancelableProgressBar(
                    "ReferenceRadar - Scanning",
                    $"[{i + 1}/{validPaths.Count}] {path}",
                    progress);

                if (isCancelled)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.Log($"[ReferenceRadar] scan cancelled at {i + 1}/{validPaths.Count}");
                    return scannedCount;
                }

                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }
                activeGuids.Add(guid);

                var type = ContentScanner.ClassifyAsset(path);
                var entry = AddEntry(guid);
                entry.Type = type;

                var currentTimestamp = GetFileTimestamp(path);
                var isDirty = entry.FileTimestamp != currentTimestamp || isForce;

                if (isDirty)
                {
                    var isReadable = type != AssetType.NonReadable && type != AssetType.Unknown;
                    if (isReadable)
                    {
                        ContentScanner.ScanEntry(entry, path);
                        scannedCount++;
                    }
                    else
                    {
                        entry.ClearUseGuids();
                    }

                    entry.FileTimestamp = currentTimestamp;
                }
            }

            RemoveStaleEntries(activeGuids);
            EditorUtility.ClearProgressBar();
            return scannedCount;
        }

        private void RemoveStaleEntries(HashSet<string> activeGuids)
        {
            var staleGuids = new List<string>();
            foreach (var entry in _data.Entries)
            {
                if (!activeGuids.Contains(entry.Guid))
                {
                    staleGuids.Add(entry.Guid);
                }
            }
            foreach (var guid in staleGuids)
            {
                RemoveEntry(guid);
            }
            if (staleGuids.Count > 0)
            {
                Debug.Log($"[ReferenceRadar] removed {staleGuids.Count} stale entries");
            }
        }

        internal static long GetFileTimestamp(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                return Directory.GetLastWriteTimeUtc(fullPath).Ticks;
            }
            if (File.Exists(fullPath))
            {
                return File.GetLastWriteTimeUtc(fullPath).Ticks;
            }
            return 0;
        }

        public void BuildUsedByMap()
        {
            _pathToGuidMap.Clear();
            foreach (var entry in _data.Entries)
            {
                entry.InitializeUsedByMap();
                var path = AssetDatabase.GUIDToAssetPath(entry.Guid);
                if (!string.IsNullOrEmpty(path))
                {
                    _pathToGuidMap[path] = entry.Guid;
                }
            }
            foreach (var assetA in _data.Entries)
            {
                // Folders as referrers add noise - skip them
                if (assetA.Type == AssetType.Folder)
                {
                    continue;
                }
                foreach (var refGuid in assetA.UseGuids)
                {
                    var assetB = GetEntry(refGuid);
                    if (assetB != null)
                    {
                        assetB.AddUsedBy(assetA);
                    }
                }
            }
        }

        private void BuildAssetMap()
        {
            _assetMap.Clear();
            foreach (var entry in _data.Entries)
            {
                entry.RebuildUseGuidsSet();
                entry.InitializeUsedByMap();
                _assetMap[entry.Guid] = entry;
            }
        }

        // --- JSON ---

        private void SaveJson(string path)
        {
            var json = JsonUtility.ToJson(_data, false);
            File.WriteAllText(path, json);
        }

        private bool LoadFromJson(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                _data = JsonUtility.FromJson<CacheData>(json);
                if (_data == null)
                {
                    _data = new CacheData();
                    Debug.LogError($"[ReferenceRadar] failed to deserialize cache from {path}");
                    return false;
                }
                BuildAssetMap();
                return true;
            }
            catch (Exception e)
            {
                _data = new CacheData();
                Debug.LogError($"[ReferenceRadar] failed to load JSON cache from {path}: {e.Message}");
                return false;
            }
        }

        // --- Binary ---

        private void SaveBinary(string path)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(BinaryMagic);
            writer.Write(BinaryVersion);
            writer.Write(_data.Entries.Count);
            foreach (var entry in _data.Entries)
            {
                WriteGuidBytes(writer, entry.Guid);
                writer.Write((byte)entry.Type);
                writer.Write(entry.FileTimestamp);
                writer.Write(entry.UseGuids.Count);
                foreach (var useGuid in entry.UseGuids)
                {
                    WriteGuidBytes(writer, useGuid);
                }
            }
        }

        private void LoadBinary(string path)
        {
            _data = new CacheData();
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var magic = reader.ReadUInt32();
            if (magic != BinaryMagic)
            {
                throw new InvalidDataException($"invalid magic number: 0x{magic:X8}, expected 0x{BinaryMagic:X8}");
            }
            var version = reader.ReadInt32();
            if (version > BinaryVersion)
            {
                throw new InvalidDataException($"unsupported cache version: {version}, max supported: {BinaryVersion}");
            }

            var entryCount = reader.ReadInt32();
            for (var i = 0; i < entryCount; i++)
            {
                var guid = ReadGuidString(reader);
                var type = (AssetType)reader.ReadByte();
                var timestamp = reader.ReadInt64();
                var useCount = reader.ReadInt32();
                var entry = new AssetEntry(guid);
                entry.Type = type;
                entry.FileTimestamp = timestamp;
                for (var j = 0; j < useCount; j++)
                {
                    entry.AddUseGuid(ReadGuidString(reader));
                }
                _data.AddEntry(entry);
            }
        }

        // --- GUID binary encoding ---

        private static void WriteGuidBytes(BinaryWriter writer, string hex)
        {
            for (var i = 0; i < 16; i++)
            {
                var hi = HexCharToNibble(hex[i * 2]);
                var lo = HexCharToNibble(hex[i * 2 + 1]);
                writer.Write((byte)((hi << 4) | lo));
            }
        }

        private static string ReadGuidString(BinaryReader reader)
        {
            var chars = new char[32];
            for (var i = 0; i < 16; i++)
            {
                var b = reader.ReadByte();
                chars[i * 2] = NibbleToHexChar(b >> 4);
                chars[i * 2 + 1] = NibbleToHexChar(b & 0xF);
            }
            return new string(chars);
        }

        private static int HexCharToNibble(char c)
        {
            if (c >= '0' && c <= '9') { return c - '0'; }
            if (c >= 'a' && c <= 'f') { return c - 'a' + 10; }
            if (c >= 'A' && c <= 'F') { return c - 'A' + 10; }
            return 0;
        }

        private static char NibbleToHexChar(int nibble)
        {
            return nibble < 10 ? (char)('0' + nibble) : (char)('a' + nibble - 10);
        }
    }
}
