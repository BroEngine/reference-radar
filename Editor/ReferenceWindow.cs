using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bro.ReferenceRadar
{
    public class ReferenceWindow : EditorWindow
    {
        private static readonly string[] ModeLabels = { "Used By", "Uses" };
        private static readonly Comparison<ResultEntry> CompareByPath = (a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal);

        private int _mode;
        private Vector2 _scrollPosition;
        private readonly List<ResultEntry> _results = new();
        private string[] _selectedGuids = Array.Empty<string>();
        [NonSerialized] private GUIStyle _rowStyle;
        private double _lastCacheCheckTime;
        
        private bool _isSceneSelection;
        private GameObject _selectedGameObject;
        private readonly List<SceneResultEntry> _sceneResults = new();

        private struct ResultEntry
        {
            public string Guid;
            public string Path;
        }

        private struct SceneResultEntry
        {
            public GameObject GameObject;
            public string ComponentName;
        }

        [MenuItem("Window/ReferenceRadar")]
        private static void OpenWindow()
        {
            GetWindow<ReferenceWindow>("ReferenceRadar");
        }

        private void OnEnable()
        {
            ProjectOverlay.CacheUpdated -= OnCacheUpdated;
            ProjectOverlay.CacheUpdated += OnCacheUpdated;

            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;

            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.sceneClosed += OnSceneClosed;

            Undo.undoRedoPerformed -= InvalidateSceneMap;
            Undo.undoRedoPerformed += InvalidateSceneMap;

            EditorApplication.hierarchyChanged -= InvalidateSceneMap;
            EditorApplication.hierarchyChanged += InvalidateSceneMap;
        }

        private void OnDisable()
        {
            ProjectOverlay.CacheUpdated -= OnCacheUpdated;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            Undo.undoRedoPerformed -= InvalidateSceneMap;
            EditorApplication.hierarchyChanged -= InvalidateSceneMap;
        }

        private void OnCacheUpdated()
        {
            RebuildResults();
            Repaint();
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            InvalidateSceneMap();
        }

        private void OnSceneClosed(Scene scene)
        {
            InvalidateSceneMap();
        }

        private void InvalidateSceneMap()
        {
            _sceneResults.Clear();

            if (_isSceneSelection)
            {
                RebuildResults();
            }

            Repaint();
        }

        private void OnSelectionChange()
        {
            _sceneResults.Clear();
            var activeGo = Selection.activeGameObject;
            var isSceneObject = activeGo != null && activeGo.scene.IsValid() && !EditorUtility.IsPersistent(activeGo);
            if (isSceneObject)
            {
                _isSceneSelection = true;
                _selectedGameObject = activeGo;
                _selectedGuids = Array.Empty<string>();
                RebuildResults();
                Repaint();
                return;
            }
            _isSceneSelection = false;
            _selectedGameObject = null;
            var guids = Selection.assetGUIDs;
            if (guids == null || guids.Length == 0)
            {
                _selectedGuids = Array.Empty<string>();
                _results.Clear();
                Repaint();
                return;
            }
            _selectedGuids = guids;
            RebuildResults();
            Repaint();
        }

        private void RebuildResults()
        {
            _results.Clear();
            if (_isSceneSelection)
            {
                RebuildSceneObjectResults();
                return;
            }
            if (!ProjectOverlay.IsReady)
            {
                return;
            }
            var cache = ProjectOverlay.Cache;
            var seen = new HashSet<string>();
            foreach (var guid in _selectedGuids)
            {
                seen.Add(guid);
            }
            if (_mode == 0)
            {
                CollectUsedBy(cache, seen);
            }
            else
            {
                CollectUses(cache, seen);
            }
            _results.Sort(CompareByPath);
        }

        private void RebuildSceneObjectResults()
        {
            if (_selectedGameObject == null)
            {
                return;
            }

            _sceneResults.Add(new SceneResultEntry
            {
                GameObject = _selectedGameObject,
                ComponentName = "Self"
            });
            if (_mode == 1)
            {
                // Uses: asset references
                var assetPaths = new List<string>();
                SceneScanner.ScanGameObject(_selectedGameObject, assetPaths);
                foreach (var path in assetPaths)
                {
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid))
                    {
                        continue;
                    }
                    _results.Add(new ResultEntry { Guid = guid, Path = path });
                }
                _results.Sort(CompareByPath);
                // Uses: scene object references
                var sceneRefs = SceneScanner.ScanGameObjectSceneRefs(_selectedGameObject);
                foreach (var sceneRef in sceneRefs)
                {
                    _sceneResults.Add(new SceneResultEntry
                    {
                        GameObject = sceneRef.GameObject,
                        ComponentName = sceneRef.ComponentName
                    });
                }
            }
            else
            {
                // Used By: find other scene objects that reference this GO
                var refs = SceneScanner.FindReferencesToObject(_selectedGameObject);
                foreach (var sceneRef in refs)
                {
                    _sceneResults.Add(new SceneResultEntry
                    {
                        GameObject = sceneRef.GameObject,
                        ComponentName = sceneRef.ComponentName
                    });
                }
            }
        }

        private void CollectUsedBy(Cache cache, HashSet<string> seen)
        {
            foreach (var selectedGuid in _selectedGuids)
            {
                var entry = cache.GetEntry(selectedGuid);
                if (entry == null)
                {
                    continue;
                }
                foreach (var kvp in entry.UsedByMap)
                {
                    if (!seen.Add(kvp.Key))
                    {
                        continue;
                    }
                    var path = AssetDatabase.GUIDToAssetPath(kvp.Key);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }
                    _results.Add(new ResultEntry { Guid = kvp.Key, Path = path });
                }
            }
        }

        private void CollectUses(Cache cache, HashSet<string> seen)
        {
            foreach (var selectedGuid in _selectedGuids)
            {
                var entry = cache.GetEntry(selectedGuid);
                if (entry == null)
                {
                    continue;
                }
                foreach (var guid in entry.UseGuids)
                {
                    if (!seen.Add(guid))
                    {
                        continue;
                    }
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }
                    _results.Add(new ResultEntry { Guid = guid, Path = path });
                }
            }
        }

        private void OnGUI()
        {
            var time = EditorApplication.timeSinceStartup;
            if (time - _lastCacheCheckTime > 2.0)
            {
                _lastCacheCheckTime = time;
                ProjectOverlay.CheckCacheFile();
            }
            
            if (!ProjectOverlay.IsReady)
            {
                if (ProjectOverlay.IsScanning)
                {
                    EditorGUILayout.HelpBox("Scanning project...", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Cache is not ready. Run a scan first.", MessageType.Info);
                    if (GUILayout.Button("Scan Project"))
                    {
                        PerformScan(false);
                    }
                }
                return;
            }
            
            DrawToolbar();
            DrawSelectionHeader();
       
            if (_isSceneSelection)
            {
                if (_selectedGameObject == null)
                {
                    EditorGUILayout.HelpBox("Selected scene object was destroyed.", MessageType.Warning);
                    return;
                }
                EnsureStyles();
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                if (_sceneResults.Count > 0)
                {
                    EditorGUILayout.LabelField("Scene References", EditorStyles.boldLabel);
                    foreach (var result in _sceneResults)
                    {
                        if (result.GameObject == null)
                        {
                            continue;
                        }
                        DrawSceneResultRow(result);
                    }
                }
                if (_mode == 1 && _results.Count > 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Asset References", EditorStyles.boldLabel);
                    foreach (var result in _results)
                    {
                        DrawResultRow(result);
                    }
                }
                EditorGUILayout.EndScrollView();
                DrawFooter();
                return;
            }
            if (_selectedGuids.Length == 0)
            {
                EditorGUILayout.HelpBox("Select an asset in the Project window or an object in the Scene.", MessageType.Info);
                DrawFooter();
                return;
            }
            DrawResults();
            DrawFooter();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.Space(2);
            var newMode = GUILayout.Toolbar(_mode, ModeLabels);
            if (newMode != _mode)
            {
                _mode = newMode;
                _sceneResults.Clear();
                RebuildResults();
            }
            EditorGUILayout.Space(2);
        }

        private void DrawSelectionHeader()
        {
            if (_isSceneSelection)
            {
                if (_selectedGameObject != null)
                {
                    EditorGUILayout.LabelField("Selected (Scene)", _selectedGameObject.name, EditorStyles.boldLabel);
                    if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    {
                        EditorGUIUtility.PingObject(_selectedGameObject);
                        Selection.activeGameObject = _selectedGameObject;
                        Event.current.Use();
                    }
                }
                return;
            }
            
            if (_selectedGuids.Length == 0)
            {
                return;
            }
            
            if (_selectedGuids.Length == 1)
            {
                var filePath = AssetDatabase.GUIDToAssetPath(_selectedGuids[0]);
                var fileName = Path.GetFileName(filePath);
                EditorGUILayout.LabelField("Selected", fileName, EditorStyles.boldLabel);
                if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(filePath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                        Selection.activeObject = asset;
                    }
                    Event.current.Use();
                }
            }
            else
            {
                EditorGUILayout.LabelField("Selected", $"{_selectedGuids.Length} assets", EditorStyles.boldLabel);
            }
        }

        private void DrawSceneResultRow(SceneResultEntry result)
        {
            var label = $" {result.GameObject.name}  ({result.ComponentName})";
            var icon = EditorGUIUtility.ObjectContent(result.GameObject, typeof(GameObject)).image;
            var content = new GUIContent(label, icon, GetGameObjectPath(result.GameObject));
            if (GUILayout.Button(content, _rowStyle, GUILayout.Height(20)))
            {
                EditorGUIUtility.PingObject(result.GameObject);
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private void DrawResults()
        {
            EnsureStyles();
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            const float rowHeight = 20f;
            var total = _results.Count;
            if (total > 0)
            {
                var viewHeight = position.height;
                var firstVisible = Mathf.Clamp(Mathf.FloorToInt(_scrollPosition.y / rowHeight), 0, total - 1);
                var lastVisible = Mathf.Clamp(Mathf.CeilToInt((_scrollPosition.y + viewHeight) / rowHeight), 0, total - 1);
                if (firstVisible > 0)
                {
                    GUILayout.Space(firstVisible * rowHeight);
                }
                for (var i = firstVisible; i <= lastVisible; i++)
                {
                    DrawResultRow(_results[i]);
                }
                var remaining = total - lastVisible - 1;
                if (remaining > 0)
                {
                    GUILayout.Space(remaining * rowHeight);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawResultRow(ResultEntry result)
        {
            var fileName = Path.GetFileName(result.Path);
            var icon = AssetDatabase.GetCachedIcon(result.Path);
            var content = new GUIContent(" " + fileName, icon, result.Path);
            if (GUILayout.Button(content, _rowStyle, GUILayout.Height(20)))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(result.Path);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            
            var label = _sceneResults.Count > 0
                ? $"{_results.Count} project, {_sceneResults.Count} scene references"
                : $"{_results.Count} references";
            
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            var settingsIcon = EditorGUIUtility.IconContent("d_Settings");
            settingsIcon.tooltip = "Settings";
            if (GUILayout.Button(settingsIcon, EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18)))
            {
                SelectOrCreateSettings();
            }
            var refreshIcon = EditorGUIUtility.IconContent("d_Refresh");
            refreshIcon.tooltip = "Rescan Project";
            if (GUILayout.Button(refreshIcon, EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18)))
            {
                PerformScan(false);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void SelectOrCreateSettings()
        {
            var settings = Settings.Instance;
            if (settings == null)
            {
                settings = CreateInstance<Settings>();
                AssetDatabase.CreateAsset(settings, "Assets/ReferenceRadarSettings.asset");
                AssetDatabase.SaveAssets();
                Settings.ResetSearch();
            }
            EditorGUIUtility.PingObject(settings);
            Selection.activeObject = settings;
        }

        private void PerformScan(bool isForce)
        {
            ProjectOverlay.StartFullScan(isForce);
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null)
            {
                return;
            }
            
            var isProSkin = EditorGUIUtility.isProSkin;
            var textColor = isProSkin
                ? new Color(0.82f, 0.82f, 0.82f, 1f)
                : new Color(0.02f, 0.02f, 0.02f, 1f);
         
            _rowStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleLeft,
                imagePosition = ImagePosition.ImageLeft,
                fixedHeight = 0,
                padding = new RectOffset(4, 4, 2, 2),
                font = EditorStyles.label.font,
                fontSize = EditorStyles.label.fontSize,
            };
            
            _rowStyle.normal.textColor = textColor;
            _rowStyle.hover.textColor = textColor;
            _rowStyle.active.textColor = textColor;
            _rowStyle.focused.textColor = textColor;
            _rowStyle.onNormal.textColor = textColor;
            _rowStyle.onHover.textColor = textColor;
            _rowStyle.onActive.textColor = textColor;
            _rowStyle.onFocused.textColor = textColor;
        }
    }
}
