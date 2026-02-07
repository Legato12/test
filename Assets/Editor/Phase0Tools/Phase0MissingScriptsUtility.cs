#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Phase0.EditorTools {
    public static class Phase0MissingScriptsUtility {
        [MenuItem("Tools/Phase0/Missing Scripts/Scan Active Scene")]
        public static void ScanActiveScene() {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) {
                Debug.LogError("[Phase0 Missing Scripts] Active scene is not loaded or invalid.");
                return;
            }

            var affected = new List<(string path, int count)>();
            int totalMissing = 0;
            int totalObjects = 0;

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots) {
                Traverse(root.transform, "", ref totalObjects, ref totalMissing, affected);
            }

            Debug.Log($"[Phase0 Missing Scripts] Scan complete for scene '{scene.name}'. Objects scanned: {totalObjects}, Missing scripts: {totalMissing}, Affected GameObjects: {affected.Count}.");
            foreach (var a in affected) {
                Debug.LogWarning($"[Phase0 Missing Scripts] {a.path} -> missing components: {a.count}");
            }
        }

        [MenuItem("Tools/Phase0/Missing Scripts/Remove In Active Scene (With Undo)")]
        public static void RemoveInActiveSceneWithUndo() {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) {
                Debug.LogError("[Phase0 Missing Scripts] Active scene is not loaded or invalid.");
                return;
            }

            int removedTotal = 0;
            int touchedObjects = 0;
            var roots = scene.GetRootGameObjects();

            foreach (var root in roots) {
                RemoveRecursive(root.transform, ref removedTotal, ref touchedObjects);
            }

            if (removedTotal > 0) {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Debug.Log($"[Phase0 Missing Scripts] Remove complete for scene '{scene.name}'. Removed components: {removedTotal}, Touched GameObjects: {touchedObjects}.");
        }

        static void Traverse(Transform t, string parentPath, ref int totalObjects, ref int totalMissing, List<(string path, int count)> affected) {
            if (t == null) return;

            totalObjects++;
            string path = string.IsNullOrEmpty(parentPath) ? t.name : (parentPath + "/" + t.name);
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (missing > 0) {
                totalMissing += missing;
                affected.Add((path, missing));
            }

            for (int i = 0; i < t.childCount; i++) {
                Traverse(t.GetChild(i), path, ref totalObjects, ref totalMissing, affected);
            }
        }

        static void RemoveRecursive(Transform t, ref int removedTotal, ref int touchedObjects) {
            if (t == null) return;

            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (missing > 0) {
                Undo.RegisterCompleteObjectUndo(t.gameObject, "Remove Missing Scripts");
                int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                removedTotal += removed;
                touchedObjects++;
            }

            for (int i = 0; i < t.childCount; i++) {
                RemoveRecursive(t.GetChild(i), ref removedTotal, ref touchedObjects);
            }
        }
    }
}
#endif