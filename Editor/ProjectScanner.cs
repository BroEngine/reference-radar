using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Bro.ReferenceRadar
{
    public static class ProjectScanner
    {

        public static event Action Completed;

        private static Cache _cache;
        private static List<string> _paths;
        private static HashSet<string> _activeGuids;
        private static int _index;
        private static bool _isForce;
        private static bool _isScanning;

        public static bool IsScanning => _isScanning;

        public static void Start(Cache cache, bool isForce)
        {
            if (_isScanning)
            {
                Cancel();
            }

            _cache = cache;

            var allPaths = AssetDatabase.GetAllAssetPaths();
            var settings = Settings.Instance;
            var isScanPackages = settings == null || settings.IsScanPackages;
            var isScanProjectSettings = settings == null || settings.IsScanProjectSettings;

            _paths = new List<string>();
            foreach (var path in allPaths)
            {
                var isAssetsPath = path.StartsWith("Assets/", StringComparison.Ordinal);
                var isPackagesPath = isScanPackages && path.StartsWith("Packages/", StringComparison.Ordinal);
                var isProjectSettingsPath = isScanProjectSettings && path.StartsWith("ProjectSettings/", StringComparison.Ordinal);
                if (isAssetsPath || isPackagesPath || isProjectSettingsPath)
                {
                    _paths.Add(path);
                }
            }

            _activeGuids = new HashSet<string>();
            _index = 0;
            _isForce = isForce;
            _isScanning = true;

            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        public static void Cancel()
        {
            EditorApplication.update -= OnUpdate;
            _isScanning = false;
            _cache = null;
            _paths = null;
            _activeGuids = null;
            EditorUtility.ClearProgressBar();
        }

        private static void OnUpdate()
        {
            if (!_isScanning || _paths == null)
            {
                EditorApplication.update -= OnUpdate;
                _isScanning = false;
                return;
            }

            var sw = Stopwatch.StartNew();

            while (_index < _paths.Count && sw.ElapsedMilliseconds < Settings.BatchTimeMs)
            {
                var path = _paths[_index];
                _index++;

                try
                {
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid))
                    {
                        continue;
                    }

                    _activeGuids.Add(guid);
                    var type = ContentScanner.ClassifyAsset(path);
                    var entry = _cache.AddEntry(guid);
                    entry.Type = type;

                    var currentTimestamp = Cache.GetFileTimestamp(path);
                    var isDirty = entry.FileTimestamp != currentTimestamp || _isForce;

                    if (isDirty)
                    {
                        var isReadable = type != AssetType.NonReadable && type != AssetType.Unknown;
                        if (isReadable)
                        {
                            ContentScanner.ScanEntry(entry, path);
                        }
                        else
                        {
                            entry.ClearUseGuids();
                        }

                        entry.FileTimestamp = currentTimestamp;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ReferenceRadar] failed to scan {path}: {e.Message}");
                }
            }

            if (_paths.Count > 0)
            {
                var progress = (float)_index / _paths.Count;
                var info = $"[{_index}/{_paths.Count}]";
                var isCancelled = EditorUtility.DisplayCancelableProgressBar("ReferenceRadar - Scanning", info, progress);

                if (isCancelled)
                {
                    Debug.Log($"[ReferenceRadar] scan cancelled at {_index}/{_paths.Count}");
                    Cancel();
                    return;
                }
            }

            if (_index >= _paths.Count)
            {
                Finish();
            }
        }

        private static void Finish()
        {
            EditorApplication.update -= OnUpdate;
            EditorUtility.ClearProgressBar();

            var totalCount = _paths.Count;
            _cache.RemoveStaleEntries(_activeGuids);
            _cache.Save();
            _cache.BuildUsedByMap();

            _isScanning = false;
            _cache = null;
            _paths = null;
            _activeGuids = null;

            Debug.Log($"[ReferenceRadar] scan complete: {totalCount} assets processed");
            Completed?.Invoke();
        }
    }
}
