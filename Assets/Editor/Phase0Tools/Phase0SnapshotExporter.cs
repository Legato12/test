#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Phase0.EditorTools {
    public static class Phase0SnapshotExporter {
        // Чтобы дамп не раздувался до сотен мегабайт на сложных сценах:
        const int MaxPropertiesPerComponent = 250;
        const int MaxArrayElementsPreview = 12;
        const int MaxStringLen = 200;

        [MenuItem("Tools/Phase0/Diagnostics/Export Active Scene Snapshot (SMART)")]
        public static void ExportActiveSceneSmart() => ExportActiveScene(full: false);

        [MenuItem("Tools/Phase0/Diagnostics/Export Active Scene Snapshot (FULL)")]
        public static void ExportActiveSceneFull() => ExportActiveScene(full: true);

        [MenuItem("Tools/Phase0/Diagnostics/Export Selection Snapshot (SMART)")]
        public static void ExportSelectionSmart() => ExportSelection(full: false);

        [MenuItem("Tools/Phase0/Diagnostics/Export Selection Snapshot (FULL)")]
        public static void ExportSelectionFull() => ExportSelection(full: true);

        static void ExportActiveScene(bool full) {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) {
                EditorUtility.DisplayDialog("Snapshot exporter", "Active scene is not loaded/valid.", "OK");
                return;
            }

            var snap = BuildSceneSnapshot(scene, full);
            WriteSnapshotToFile(snap, MakeFileName($"Scene_{scene.name}", full));
        }

        static void ExportSelection(bool full) {
            UnityEngine.Object obj = Selection.activeObject;
            if (obj == null) {
                EditorUtility.DisplayDialog("Snapshot exporter", "Nothing selected. Select a GameObject in Scene or a Prefab asset in Project.", "OK");
                return;
            }

            // Selection can be a scene GameObject or a prefab asset root.
            if (obj is GameObject go) {
                if (PrefabUtility.IsPartOfPrefabAsset(go)) {
                    // Prefab asset
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path)) {
                        EditorUtility.DisplayDialog("Snapshot exporter", "Selected prefab has no asset path.", "OK");
                        return;
                    }
                    var snap = BuildPrefabSnapshot(path, full);
                    WriteSnapshotToFile(snap, MakeFileName($"Prefab_{go.name}", full));
                } else {
                    // Scene object
                    var snap = BuildSingleObjectSnapshot(go, full);
                    WriteSnapshotToFile(snap, MakeFileName($"Selection_{go.name}", full));
                }
            } else {
                EditorUtility.DisplayDialog("Snapshot exporter", $"Selection type not supported: {obj.GetType().Name}\nSelect a GameObject or Prefab.", "OK");
            }
        }

        static string MakeFileName(string prefix, bool full) {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            return $"Phase0_Snapshot_{prefix}_{(full ? "FULL" : "SMART")}_{stamp}.json";
        }

        static void WriteSnapshotToFile(Snapshot snap, string fileName) {
            string json = JsonUtility.ToJson(snap, prettyPrint: true);

            // Copy to clipboard for quick paste
            EditorGUIUtility.systemCopyBuffer = json;

            // Write to project root (one level above Assets)
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outPath = Path.Combine(projectRoot, fileName);
            File.WriteAllText(outPath, json);

            Debug.Log($"[Phase0SnapshotExporter] Snapshot written:\n{outPath}\n(Copied to clipboard too)");
            EditorUtility.RevealInFinder(outPath);
        }

        // ---------------- Snapshot builders ----------------

        static Snapshot BuildSceneSnapshot(Scene scene, bool full) {
            var snap = NewBaseSnapshot(full);
            snap.kind = "Scene";
            snap.sceneName = scene.name;
            snap.scenePath = scene.path;

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots) {
                snap.roots.Add(CaptureGameObject(root, full, parentPath: ""));
            }
            return snap;
        }

        static Snapshot BuildSingleObjectSnapshot(GameObject go, bool full) {
            var snap = NewBaseSnapshot(full);
            snap.kind = "Selection(SceneObject)";
            snap.sceneName = go.scene.name;
            snap.scenePath = go.scene.path;
            snap.roots.Add(CaptureGameObject(go, full, parentPath: ""));
            return snap;
        }

        static Snapshot BuildPrefabSnapshot(string prefabAssetPath, bool full) {
            var snap = NewBaseSnapshot(full);
            snap.kind = "PrefabAsset";
            snap.prefabAssetPath = prefabAssetPath;

            GameObject root = null;
            try {
                root = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                snap.roots.Add(CaptureGameObject(root, full, parentPath: ""));
            } finally {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
            return snap;
        }

        static Snapshot NewBaseSnapshot(bool full) {
            return new Snapshot {
                unityVersion = Application.unityVersion,
                timeUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                fullDump = full,
            };
        }

        static GameObjectNode CaptureGameObject(GameObject go, bool full, string parentPath) {
            var node = new GameObjectNode();
            node.name = go.name;
            node.path = string.IsNullOrEmpty(parentPath) ? go.name : (parentPath + "/" + go.name);
            node.activeSelf = go.activeSelf;
            node.activeInHierarchy = go.activeInHierarchy;
            node.layer = go.layer;
            node.tag = SafeGetTag(go);
            node.isStatic = go.isStatic;

            // Prefab info (helpful to understand what is instance vs asset)
            node.prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            node.prefabStatus = PrefabUtility.GetPrefabInstanceStatus(go).ToString();

            // Transform
            var t = go.transform;
            node.transform = new TransformData {
                localPosition = t.localPosition,
                localEuler = t.localEulerAngles,
                localScale = t.localScale
            };

            // Components
            var comps = go.GetComponents<Component>();
            foreach (var c in comps) {
                if (c == null) continue;
                if (c is Transform) continue; // already captured

                if (!full && !ShouldIncludeSmart(c)) {
                    continue;
                }

                var cd = new ComponentData();
                cd.type = c.GetType().FullName;

                // Script path (for MonoBehaviours)
                if (c is MonoBehaviour mb) {
                    var ms = MonoScript.FromMonoBehaviour(mb);
                    cd.scriptAssetPath = ms != null ? AssetDatabase.GetAssetPath(ms) : "";
                }

                // Key renderer info in SMART mode (materials/shader/sorting)
                if (!full) {
                    TryAddSmartRendererFields(c, cd);
                }

                // Serialized properties (safe generic)
                DumpSerializedProperties(c, cd, full);

                node.components.Add(cd);
            }

            // Children
            for (int i = 0; i < t.childCount; i++) {
                var child = t.GetChild(i).gameObject;
                node.children.Add(CaptureGameObject(child, full, node.path));
            }

            return node;
        }

        static bool ShouldIncludeSmart(Component c) {
            // SMART = всё своё + рендереры/канвасы/коллайдеры, чтобы понимать визуал/физику.
            var type = c.GetType();
            if (c is MonoBehaviour) return true;
            if (c is Renderer) return true;
            if (type.Name.Contains("Collider")) return true;
            if (type.Name.Contains("Canvas")) return true;
            if (type.Name.Contains("SortingGroup")) return true;
            if (type.Namespace != null && type.Namespace.Contains("Spine")) return true;
            return false;
        }

        static void TryAddSmartRendererFields(Component c, ComponentData cd) {
            if (c is Renderer r) {
                cd.smartHints.Add($"Renderer.enabled={r.enabled}");
                cd.smartHints.Add($"Renderer.sortingLayerID={r.sortingLayerID}");
                cd.smartHints.Add($"Renderer.sortingOrder={r.sortingOrder}");
                var mats = r.sharedMaterials;
                if (mats != null) {
                    for (int i = 0; i < mats.Length; i++) {
                        var m = mats[i];
                        if (m == null) continue;
                        string mp = AssetDatabase.GetAssetPath(m);
                        cd.smartHints.Add($"Material[{i}] name={m.name} shader={m.shader?.name} path={mp}");
                    }
                }
            }

            if (c is SpriteRenderer sr) {
                var sp = sr.sprite;
                string spPath = sp ? AssetDatabase.GetAssetPath(sp) : "";
                cd.smartHints.Add($"SpriteRenderer.sprite={(sp ? sp.name : "null")} path={spPath}");
                cd.smartHints.Add($"SpriteRenderer.color={sr.color}");
                cd.smartHints.Add($"SpriteRenderer.sortingLayerName={sr.sortingLayerName}");
                cd.smartHints.Add($"SpriteRenderer.sortingOrder={sr.sortingOrder}");
            }
        }

        static void DumpSerializedProperties(Component c, ComponentData cd, bool full) {
            try {
                var so = new SerializedObject(c);
                var it = so.GetIterator();

                bool enterChildren = true;
                int count = 0;

                while (it.NextVisible(enterChildren)) {
                    enterChildren = false;

                    if (it.name == "m_Script") continue;

                    // В SMART режиме можно чуть урезать встроенные компоненты,
                    // но для MonoBehaviour оставляем.
                    if (!full && !(c is MonoBehaviour) && (count > 40)) {
                        cd.warnings.Add("SMART: built-in component properties truncated.");
                        break;
                    }

                    if (count++ >= MaxPropertiesPerComponent) {
                        cd.warnings.Add($"Truncated at {MaxPropertiesPerComponent} properties.");
                        break;
                    }

                    var pd = new PropertyData();
                    pd.path = it.propertyPath;
                    pd.type = it.propertyType.ToString();
                    pd.value = SerializePropertyValue(it);

                    cd.properties.Add(pd);
                }
            } catch (Exception ex) {
                cd.warnings.Add("Failed to dump SerializedObject: " + ex.Message);
            }
        }

        static string SerializePropertyValue(SerializedProperty p) {
            try {
                switch (p.propertyType) {
                    case SerializedPropertyType.Integer: return p.intValue.ToString(CultureInfo.InvariantCulture);
                    case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                    case SerializedPropertyType.Float: return p.floatValue.ToString("R", CultureInfo.InvariantCulture);
                    case SerializedPropertyType.String: return Trim(p.stringValue);
                    case SerializedPropertyType.Color: return p.colorValue.ToString();
                    case SerializedPropertyType.ObjectReference:
                        var obj = p.objectReferenceValue;
                        if (obj == null) return "null";
                        string path = AssetDatabase.GetAssetPath(obj);
                        return string.IsNullOrEmpty(path)
                            ? $"{obj.name} ({obj.GetType().Name})"
                            : $"{obj.name} ({obj.GetType().Name}) @ {path}";
                    case SerializedPropertyType.Enum:
                        return (p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length)
                            ? p.enumNames[p.enumValueIndex]
                            : p.enumValueIndex.ToString();
                    case SerializedPropertyType.Vector2: return p.vector2Value.ToString();
                    case SerializedPropertyType.Vector3: return p.vector3Value.ToString();
                    case SerializedPropertyType.Vector4: return p.vector4Value.ToString();
                    case SerializedPropertyType.Quaternion: return p.quaternionValue.eulerAngles.ToString();
                    case SerializedPropertyType.Rect: return p.rectValue.ToString();
                    case SerializedPropertyType.Bounds: return p.boundsValue.ToString();
#if UNITY_2020_1_OR_NEWER
                    case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
                    case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
                    case SerializedPropertyType.RectInt: return p.rectIntValue.ToString();
                    case SerializedPropertyType.BoundsInt: return p.boundsIntValue.ToString();
#endif
                    case SerializedPropertyType.AnimationCurve:
                        return p.animationCurveValue != null ? $"AnimationCurve(keys={p.animationCurveValue.keys.Length})" : "null";
                    case SerializedPropertyType.Generic:
                        if (p.isArray && p.propertyPath.EndsWith(".Array.size", StringComparison.Ordinal)) {
                            return p.intValue.ToString(CultureInfo.InvariantCulture);
                        }
                        // Чтобы не раздувать: для массивов/структур просто отметим факт
                        if (p.isArray && p.propertyType != SerializedPropertyType.String) {
                            return $"Array(size={p.arraySize}, preview up to {MaxArrayElementsPreview})";
                        }
                        return "<Generic>";
                    default:
                        return "<Unsupported>";
                }
            } catch (Exception ex) {
                return "<ERR:" + ex.Message + ">";
            }
        }

        static string Trim(string s) {
            if (s == null) return "null";
            if (s.Length <= MaxStringLen) return s;
            return s.Substring(0, MaxStringLen) + "...";
        }

        static string SafeGetTag(GameObject go) {
            try { return go.tag; }
            catch { return "<Untagged/Invalid>"; }
        }

        // ---------------- Data ----------------

        [Serializable]
        class Snapshot {
            public string unityVersion;
            public string timeUtc;
            public bool fullDump;

            public string kind; // Scene / PrefabAsset / Selection
            public string sceneName;
            public string scenePath;
            public string prefabAssetPath;

            public List<GameObjectNode> roots = new List<GameObjectNode>();
        }

        [Serializable]
        class GameObjectNode {
            public string name;
            public string path;

            public bool activeSelf;
            public bool activeInHierarchy;
            public int layer;
            public string tag;
            public bool isStatic;

            public string prefabAssetPath;
            public string prefabStatus;

            public TransformData transform;
            public List<ComponentData> components = new List<ComponentData>();
            public List<GameObjectNode> children = new List<GameObjectNode>();
        }

        [Serializable]
        class TransformData {
            public Vector3 localPosition;
            public Vector3 localEuler;
            public Vector3 localScale;
        }

        [Serializable]
        class ComponentData {
            public string type;
            public string scriptAssetPath;
            public List<string> smartHints = new List<string>();
            public List<PropertyData> properties = new List<PropertyData>();
            public List<string> warnings = new List<string>();
        }

        [Serializable]
        class PropertyData {
            public string path;
            public string type;
            public string value;
        }
    }
}
#endif
