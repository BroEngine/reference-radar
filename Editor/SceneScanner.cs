using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bro.ReferenceRadar
{
    public struct SceneObjectRef
    {
        public GameObject GameObject;
        public string ComponentName;
    }

    public static class SceneScanner
    {
        public static void ScanGameObject(GameObject go, List<string> assetPaths)
        {
            var seen = new HashSet<string>();
            var components = go.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }
                CollectAssetReferences(component, seen, assetPaths);
            }
        }

        public static List<SceneObjectRef> ScanGameObjectSceneRefs(GameObject go)
        {
            var results = new List<SceneObjectRef>();
            var seen = new HashSet<int>();
            var components = go.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }
                
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                
                while (prop.NextVisible(true))
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
         
                    var assetPath = AssetDatabase.GetAssetPath(refObj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        continue;
                    }
                    var refGo = ResolveGameObject(refObj);
                    if (refGo == null || refGo == go)
                    {
                        continue;
                    }
                    if (!seen.Add(refGo.GetInstanceID()))
                    {
                        continue;
                    }
                    results.Add(new SceneObjectRef
                    {
                        GameObject = refGo,
                        ComponentName = component.GetType().Name
                    });
                }
            }
            return results;
        }

        public static Dictionary<string, List<SceneObjectRef>> BuildSceneUsageMap()
        {
            var map = new Dictionary<string, List<SceneObjectRef>>();
            var roots = GetAllRootGameObjects();
            foreach (var root in roots)
            {
                ScanHierarchy(root, map);
            }
            return map;
        }

        private static void ScanHierarchy(GameObject root, Dictionary<string, List<SceneObjectRef>> map)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            var seen = new HashSet<(int goId, string assetPath)>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }
                var componentName = component.GetType().Name;
                var go = component.gameObject;
                var goId = go.GetInstanceID();
                var so = new SerializedObject(component);
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
                    if (!seen.Add((goId, refPath)))
                    {
                        continue;
                    }
                    if (!map.TryGetValue(refPath, out var list))
                    {
                        list = new List<SceneObjectRef>();
                        map[refPath] = list;
                    }
                    list.Add(new SceneObjectRef
                    {
                        GameObject = go,
                        ComponentName = componentName
                    });
                }
            }
        }

        public static List<SceneObjectRef> FindReferencesToObject(GameObject target)
        {
            var results = new List<SceneObjectRef>();
            var seen = new HashSet<int>();
            var roots = GetAllRootGameObjects();
            foreach (var root in roots)
            {
                FindRefsInHierarchy(root, target, seen, results);
            }
            return results;
        }

        private static void FindRefsInHierarchy( GameObject root, GameObject target, HashSet<int> seen, List<SceneObjectRef> results)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }
                
                var go = component.gameObject;
                if (go == target)
                {
                    continue;
                }
                
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                var isFound = false;
                while (prop.NextVisible(true))
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
                    
                    var refGo = ResolveGameObject(refObj);
                    if (refGo == target)
                    {
                        isFound = true;
                        break;
                    }
                }
                if (isFound && seen.Add(go.GetInstanceID()))
                {
                    results.Add(new SceneObjectRef
                    {
                        GameObject = go,
                        ComponentName = component.GetType().Name
                    });
                }
            }
        }

        private static List<GameObject> GetAllRootGameObjects()
        {
            var roots = new List<GameObject>();
       
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                var prefabRoot = prefabStage.prefabContentsRoot;
                if (prefabRoot != null)
                {
                    roots.Add(prefabRoot);
                }
                return roots;
            }

            var sceneCount = SceneManager.sceneCount;
            for (var s = 0; s < sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }
                roots.AddRange(scene.GetRootGameObjects());
            }
            return roots;
        }

        private static GameObject ResolveGameObject(Object obj)
        {
            if (obj is GameObject go)
            {
                return go;
            }
            if (obj is Component comp)
            {
                return comp.gameObject;
            }
            return null;
        }

        private static void CollectAssetReferences(Component component, HashSet<string> seen, List<string> assetPaths)
        {
            var so = new SerializedObject(component);
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
                if (seen.Add(refPath))
                {
                    assetPaths.Add(refPath);
                }
            }
        }
    }
}
