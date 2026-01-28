// Phase0AssetsSetupEditor.cs
// Put this file under: Assets/Editor/ (or any Editor folder)
// Menu: Tools/Phase 0/Create Config Assets (SO)

using System.IO;
using UnityEditor;
using UnityEngine;

public static class Phase0AssetsSetupEditor
{
    private const string RootFolder = "Assets/Phase0";
    private const string ConfigFolder = "Assets/Phase0/Configs";
    private const string ScriptsFolder = "Assets/Phase0/Scripts";

    [MenuItem("Tools/Phase 0/Create Config Assets (SO)")]
    public static void CreateAssets()
    {
        EnsureFolder(RootFolder);
        EnsureFolder(ConfigFolder);
        EnsureFolder(ScriptsFolder);

        // Create SceneConfigSO
        var sceneCfgPath = Path.Combine(ConfigFolder, "SceneConfig.asset").Replace("\\", "/");
        if (!File.Exists(sceneCfgPath))
        {
            var cfg = ScriptableObject.CreateInstance<Phase0.SceneConfigSO>();
            cfg.gridSize = 4;
            cfg.cellSize = 1.0f;
            cfg.cellGap = 0.06f;
            cfg.tapDragThresholdPx = 10f;
            cfg.snapDuration = 0.12f;
            cfg.bounceBackDuration = 0.16f;

            AssetDatabase.CreateAsset(cfg, sceneCfgPath);
        }

        // Create ShapeDefinitionSO (L base only)
        var shapePath = Path.Combine(ConfigFolder, "Shape_L.asset").Replace("\\", "/");
        if (!File.Exists(shapePath))
        {
            var shape = ScriptableObject.CreateInstance<Phase0.ShapeDefinitionSO>();
            shape.shapeId = "L";
            shape.pivot = new Vector2Int(0, 0);
            shape.suggestedWorldScale = 1.0f;

            // Base L orientation:
            // (0,0)
            // (0,1)
            // (0,2)
            // (1,0)
            shape.baseCells = new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, 2),
                new Vector2Int(1, 0),
            };

            AssetDatabase.CreateAsset(shape, shapePath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Phase 0",
            "Created/verified:\n" +
            "- Assets/Phase0/Configs/SceneConfig.asset\n" +
            "- Assets/Phase0/Configs/Shape_L.asset\n\n" +
            "Next: runtime core will read these assets.",
            "OK"
        );
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        var parent = Path.GetDirectoryName(path).Replace("\\", "/");
        var name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, name);
    }
}
