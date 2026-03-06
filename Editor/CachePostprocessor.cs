using UnityEditor;

namespace Bro.ReferenceRadar
{
    public class CachePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var hasDirty = false;

            foreach (var path in importedAssets)
            {
                hasDirty |= EnqueueByPath(path);
            }

            foreach (var path in movedAssets)
            {
                hasDirty |= EnqueueByPath(path);
            }
            
            foreach (var path in deletedAssets)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);

                if (string.IsNullOrEmpty(guid))
                {
                    guid = ProjectOverlay.Cache.GetGuidByPath(path);
                }

                if (!string.IsNullOrEmpty(guid))
                {
                    ProjectOverlay.EnqueueDirty(guid);
                    hasDirty = true;
                }
            }

            if (hasDirty)
            {
                ProjectOverlay.StartProcessing();
            }
        }

        private static bool EnqueueByPath(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            ProjectOverlay.EnqueueDirty(guid);
            return true;
        }
    }
}
