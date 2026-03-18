using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bro.ReferenceRadar
{
    public enum CounterPosition
    {
        Left,
        Right,
    }

    public class Settings : ScriptableObject
    {
        public const int MaxPropertyIterations = 10000;
        public const long BatchTimeMs = 100;
        public const int DefaultItemsPerFrame = 10;

        private static Settings _instance;
        private static bool _isSearched;

        [Header("Scanning")]
        [Tooltip("Include Packages/ folder in project scan")]
        [SerializeField] private bool _isScanPackages = true;

        [Tooltip("Include ProjectSettings/ folder in project scan")]
        [SerializeField] private bool _isScanProjectSettings = true;

        [Header("Ignore")]
        [Tooltip("Path prefixes to ignore during scan (e.g. Assets/External/FindReference2/)")]
        [SerializeField] private List<string> _ignorePaths = new();

        [Tooltip("File names (without extension) to ignore during scan")]
        [SerializeField] private List<string> _ignoreFileNames = new();

        [Tooltip("Additional file extensions to skip (e.g. .bank, .fmod). Dot prefix optional.")]
        [SerializeField] private List<string> _extraSkipExtensions = new();

        [Header("Display")]
        [Tooltip("Show reference count overlay in the Project window")]
        [SerializeField] private bool _isShowCount = true;

        [Tooltip("Position of the reference count in the Project window")]
        [SerializeField] private CounterPosition _counterPosition = CounterPosition.Right;

        [Header("Cache")]
        [Tooltip("Path to the cache file relative to project root")]
        [SerializeField] private string _cachePath = "Library/ReferenceRadarCache.bin";

        [Header("Performance")]
        [Tooltip("Number of dirty assets to process per editor frame during async updates")]
        [SerializeField] [Range(1, 50)] private int _itemsPerFrame = 10;

        public bool IsScanPackages => _isScanPackages;
        public bool IsScanProjectSettings => _isScanProjectSettings;
        public IReadOnlyList<string> IgnorePaths => _ignorePaths;
        public IReadOnlyList<string> IgnoreFileNames => _ignoreFileNames;
        public IReadOnlyList<string> ExtraSkipExtensions => _extraSkipExtensions;
        public CounterPosition CounterPosition => _counterPosition;
        public bool IsShowCount
        {
            get => _isShowCount;
            set
            {
                _isShowCount = value;
                EditorUtility.SetDirty(this);
            }
        }
        public string CachePath => string.IsNullOrEmpty(_cachePath) ? "Library/ReferenceRadarCache.bin" : _cachePath;
        public int ItemsPerFrame => _itemsPerFrame;

        public static Settings Instance
        {
            get
            {
                if (!_isSearched)
                {
                    _isSearched = true;
                    var guids = AssetDatabase.FindAssets("t:Bro.ReferenceRadar.Settings");
                    if (guids.Length > 0)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        _instance = AssetDatabase.LoadAssetAtPath<Settings>(path);
                    }
                }
                return _instance;
            }
        }

        public static void ResetSearch()
        {
            _isSearched = false;
            _instance = null;
        }
    }
}
