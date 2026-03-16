using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bro.ReferenceRadar
{
    [InitializeOnLoad]
    public static class ProjectOverlay
    {
        private const int DefaultItemsPerFrame = 10;

        public static event Action CacheUpdated;

        private static Cache _cache;
        private static bool _isInitialized;
        private static bool _isReady;
        private static GUIStyle _labelStyle;

        private static readonly Queue<string> _dirtyQueue = new();
        private static readonly HashSet<string> _dirtySet = new();
        private static bool _isProcessing;

        static ProjectOverlay()
        {
            EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            ProjectScanner.Completed -= OnScanCompleted;
            ProjectScanner.Completed += OnScanCompleted;
        }

        public static Cache Cache
        {
            get
            {
                EnsureInitialized();
                return _cache;
            }
        }

        public static bool IsReady => _isReady;
        public static bool IsScanning => ProjectScanner.IsScanning;

        public static void CheckCacheFile()
        {
            if (_isReady && !File.Exists(Cache.CachePath))
            {
                _isReady = false;
                StopProcessing();
                EditorApplication.RepaintProjectWindow();
            }
        }

        public static bool IsShowCountEnabled
        {
            get => Settings.Instance?.IsShowCount ?? true;
            set
            {
                var settings = Settings.Instance;
                if (settings != null)
                {
                    settings.IsShowCount = value;
                }
                EditorApplication.RepaintProjectWindow();
            }
        }

        public static void RebuildUsedByMap()
        {
            EnsureInitialized();
            _cache.BuildUsedByMap();
            _isReady = true;
            EditorApplication.RepaintProjectWindow();
            CacheUpdated?.Invoke();
        }

        public static void EnqueueDirty(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            if (!_isReady || ProjectScanner.IsScanning)
            {
                return;
            }

            if (_dirtySet.Add(guid))
            {
                _dirtyQueue.Enqueue(guid);
            }
        }

        public static void StartProcessing()
        {
            if (_isProcessing)
            {
                return;
            }

            if (_dirtyQueue.Count == 0)
            {
                return;
            }

            _isProcessing = true;
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        public static void StopProcessing()
        {
            EditorApplication.update -= OnUpdate;
            _isProcessing = false;
            _dirtyQueue.Clear();
            _dirtySet.Clear();
        }

        public static void StartFullScan(bool isForce)
        {
            StopProcessing();
            EnsureInitialized();
            ProjectScanner.Start(_cache, isForce);
        }

        private static void OnScanCompleted()
        {
            _isReady = true;
            EditorApplication.RepaintProjectWindow();
            CacheUpdated?.Invoke();

            if (_dirtyQueue.Count > 0)
            {
                StartProcessing();
            }
        }

        private static void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }
            _isInitialized = true;
            _cache = new Cache();
            var isLoaded = _cache.Load();
            if (isLoaded)
            {
                _cache.BuildUsedByMap();
                _isReady = true;
            }
        }

        private static void OnUpdate()
        {
            if (_dirtyQueue.Count == 0)
            {
                FinishProcessing();
                return;
            }
            var settings = Settings.Instance;
            var itemsPerFrame = settings != null ? settings.ItemsPerFrame : DefaultItemsPerFrame;
            var count = 0;
            while (_dirtyQueue.Count > 0 && count < itemsPerFrame)
            {
                var guid = _dirtyQueue.Dequeue();
                _dirtySet.Remove(guid);
                ProcessDirtyEntry(guid);
                count++;
            }
            if (_dirtyQueue.Count == 0)
            {
                FinishProcessing();
            }
        }

        private static void ProcessDirtyEntry(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            // Asset was deleted
            if (string.IsNullOrEmpty(path))
            {
                _cache.RemoveEntry(guid);
                return;
            }
            var type = ContentScanner.ClassifyAsset(path);
            var entry = _cache.AddEntry(guid);
            entry.Type = type;
            entry.FileTimestamp = Cache.GetFileTimestamp(path);
            var isReadable = type != AssetType.NonReadable && type != AssetType.Unknown;
            if (isReadable)
            {
                ContentScanner.ScanEntry(entry, path);
            }
            else
            {
                entry.ClearUseGuids();
            }
        }

        private static void FinishProcessing()
        {
            EditorApplication.update -= OnUpdate;
            _isProcessing = false;
            if (!_isReady || !File.Exists(Cache.CachePath))
            {
                _isReady = false;
                return;
            }
            _cache.BuildUsedByMap();
            _cache.Save();
            EditorApplication.RepaintProjectWindow();
            CacheUpdated?.Invoke();
        }

        private static void OnProjectWindowItemGUI(string guid, Rect rect)
        {
            if (!IsShowCountEnabled)
            {
                return;
            }
            if (!_isInitialized)
            {
                EnsureInitialized();
            }
            if (!_isReady)
            {
                return;
            }
            var entry = _cache.GetEntry(guid);
            if (entry == null)
            {
                return;
            }
            var count = entry.UsedByMap.Count;
            if (count == 0)
            {
                return;
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.miniLabel);
            }
            var settings = Settings.Instance;
            var isLeft = settings != null && settings.CounterPosition == CounterPosition.Left;
            if (isLeft)
            {
                rect = new Rect(rect.x - 30, rect.y, 28, rect.height);
            }
            _labelStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(rect, count.ToString(), _labelStyle);
        }
    }
}
