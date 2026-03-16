using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bro.ReferenceRadar
{
    public static class ContentScanner
    {
        private static readonly HashSet<string> SkipExtensions = new()
        {
            // Scripts & compiled
            ".cs", ".dll", ".js", ".boo", ".asmdef", ".asmref", ".rsp",
            // Images
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".tif", ".tiff",
            ".psd", ".exr", ".hdr", ".svg", ".ico",
            // Audio
            ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac", ".mod", ".it", ".s3m", ".xm",
            // Video
            ".mp4", ".mov", ".avi", ".webm", ".wmv",
            // Fonts
            ".ttf", ".otf",
            // Shaders (not YAML, no guid: patterns in content)
            ".shader", ".cginc", ".hlsl", ".compute",
            // Raw text (no GUID references)
            ".txt", ".md", ".json", ".xml", ".csv", ".html", ".css", ".yml", ".yaml",
            // Archives & binary blobs
            ".bytes", ".zip", ".rar", ".7z",
        };

        public static AssetType ClassifyAsset(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return AssetType.Folder;
            }
            var settings = Settings.Instance;
            if (IsIgnored(path, settings))
            {
                return AssetType.NonReadable;
            }
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                return AssetType.NonReadable;
            }
            var isSkipped = SkipExtensions.Contains(ext) || IsExtraSkipExtension(ext, settings);
            if (isSkipped)
            {
                return AssetType.NonReadable;
            }
            if (IsYamlFile(path))
            {
                return AssetType.Yaml;
            }
            return AssetType.Binary;
        }

        private static bool IsIgnored(string path, Settings settings)
        {
            if (settings == null)
            {
                return false;
            }
            var fileName = Path.GetFileNameWithoutExtension(path);
            foreach (var ignoreName in settings.IgnoreFileNames)
            {
                if (!string.IsNullOrEmpty(ignoreName) && fileName == ignoreName)
                {
                    return true;
                }
            }
            foreach (var ignorePath in settings.IgnorePaths)
            {
                if (!string.IsNullOrEmpty(ignorePath) && path.StartsWith(ignorePath, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsExtraSkipExtension(string ext, Settings settings)
        {
            if (settings == null)
            {
                return false;
            }
            foreach (var skipExt in settings.ExtraSkipExtensions)
            {
                if (string.IsNullOrEmpty(skipExt))
                {
                    continue;
                }
                // Normalize: handle both ".bank" and "bank" input
                var normalizedExt = skipExt[0] == '.' ? skipExt : "." + skipExt;
                if (ext == normalizedExt.ToLowerInvariant())
                {
                    return true;
                }
            }
            return false;
        }

        public static void ScanEntry(AssetEntry entry, string path)
        {
            entry.ClearUseGuids();

            switch (entry.Type)
            {
                case AssetType.Yaml:
                    ScanYaml(entry, path);
                    break;
                case AssetType.Binary:
                    ScanBinary(entry, path);
                    break;
                case AssetType.Folder:
                    ScanFolder(entry, path);
                    break;
            }
        }

        private static void ScanYaml(AssetEntry entry, string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return;
            }
            try
            {
                using var reader = new StreamReader(fullPath);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var idx = line.IndexOf("guid: ", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        TryExtractAndAddGuid(line, idx + 6, entry);
                    }
                    idx = line.IndexOf("m_AssetGUID: ", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        TryExtractAndAddGuid(line, idx + 13, entry);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReferenceRadar] failed to scan YAML {path}: {e.Message}");
            }
        }

        private static void ScanBinary(AssetEntry entry, string path)
        {
            try
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                {
                    return;
                }
                if (asset is GameObject go)
                {
                    var components = go.GetComponentsInChildren<Component>(true);
                    foreach (var component in components)
                    {
                        if (component == null)
                        {
                            continue;
                        }
                        ScanSerializedObject(component, entry);
                    }
                }
                else if (asset is TerrainData terrain)
                {
                    ScanTerrainData(terrain, entry);
                }
                else
                {
                    ScanSerializedObject(asset, entry);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReferenceRadar] failed to scan binary {path}: {e.Message}");
            }
        }

        private static void ScanSerializedObject(UnityEngine.Object obj, AssetEntry entry)
        {
            var so = new SerializedObject(obj);
            var prop = so.GetIterator();
            while (prop.Next(true))
            {
                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }
                var refObj = prop.objectReferenceValue;
                if (refObj == null)
                {
                    continue;
                }
                var refPath = AssetDatabase.GetAssetPath(refObj);
                if (string.IsNullOrEmpty(refPath))
                {
                    continue;
                }
                entry.AddUseGuid(AssetDatabase.AssetPathToGUID(refPath));
            }
        }

        private static void ScanTerrainData(TerrainData terrain, AssetEntry entry)
        {
            if (terrain.terrainLayers != null)
            {
                foreach (var layer in terrain.terrainLayers)
                {
                    if (layer == null)
                    {
                        continue;
                    }
                    AddObjectGuid(layer, entry);
                    AddObjectGuid(layer.diffuseTexture, entry);
                    AddObjectGuid(layer.normalMapTexture, entry);
                    AddObjectGuid(layer.maskMapTexture, entry);
                }
            }
            if (terrain.detailPrototypes != null)
            {
                foreach (var detail in terrain.detailPrototypes)
                {
                    AddObjectGuid(detail.prototypeTexture, entry);
                    AddObjectGuid(detail.prototype, entry);
                }
            }
            if (terrain.treePrototypes != null)
            {
                foreach (var tree in terrain.treePrototypes)
                {
                    AddObjectGuid(tree.prefab, entry);
                }
            }
            // Catch anything the explicit checks above missed
            ScanSerializedObject(terrain, entry);
        }

        private static void AddObjectGuid(UnityEngine.Object obj, AssetEntry entry)
        {
            if (obj == null)
            {
                return;
            }
            var objPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(objPath))
            {
                return;
            }
            entry.AddUseGuid(AssetDatabase.AssetPathToGUID(objPath));
        }

        private static void ScanFolder(AssetEntry entry, string path)
        {
            // Sub-folders
            foreach (var subFolder in AssetDatabase.GetSubFolders(path))
            {
                entry.AddUseGuid(AssetDatabase.AssetPathToGUID(subFolder));
            }
            // Files in folder (non-recursive)
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return;
            }
            foreach (var file in Directory.GetFiles(fullPath))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var relativePath = FileUtil.GetProjectRelativePath(file);
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }
                entry.AddUseGuid(AssetDatabase.AssetPathToGUID(relativePath));
            }
        }

        private static bool IsYamlFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return false;
            }
            try
            {
                using var stream = File.OpenRead(fullPath);
                var buffer = new byte[5];
                var bytesRead = stream.Read(buffer, 0, 5);
                if (bytesRead < 5)
                {
                    return false;
                }
                var isYamlHeader = buffer[0] == '%' && buffer[1] == 'Y' && buffer[2] == 'A' && buffer[3] == 'M' && buffer[4] == 'L';
                return isYamlHeader;
            }
            catch
            {
                return false;
            }
        }

        private static void TryExtractAndAddGuid(string line, int startIndex, AssetEntry entry)
        {
            if (startIndex + 32 > line.Length)
            {
                return;
            }
            for (var i = startIndex; i < startIndex + 32; i++)
            {
                if (!IsHexChar(line[i]))
                {
                    return;
                }
            }
            var guid = line.Substring(startIndex, 32);
            if (IsBuiltInGuid(guid))
            {
                return;
            }
            entry.AddUseGuid(guid);
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        // Built-in Unity assets have GUIDs like "0000000000000000f000000000000000"
        private static bool IsBuiltInGuid(string guid)
        {
            for (var i = 0; i < 16; i++)
            {
                if (guid[i] != '0')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
