// Phase0SceneSetup.cs
// Drop this file anywhere under Assets/Editor/ and run:
// Tools/Phase 0/Setup Scene (4x4 Grid + 2D Ortho + placeholders)
//
// This creates a clean, world-space 2D top-down scene skeleton for the
// "Unity Prototype + Spine Rigging Test" (Phase 0).
//
// Note: This script intentionally does NOT implement gameplay logic.
// It only scaffolds the scene objects, hierarchy, and basic visuals.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Phase0SceneSetup
{
    private const string RootName = "Phase0_Root";
    private const string GeneratedFolder = "Assets/Phase0/Generated";
    private const string WhiteTexturePath = GeneratedFolder + "/white_32.png";

    // Grid settings (can tweak after creation)
    private const int GridSize = 4;              // 4x4 as requested
    private const float CellSize = 1.0f;         // world units
    private const float CellGap = 0.06f;         // small spacing for readability
    private static readonly Vector2 GridCenter = new Vector2(0f, 0f);

    // Example blocked cells on the grid (row/col, 0-based, bottom-left origin)
    private static readonly Vector2Int[] DefaultBlockedCells = new[]
    {
        new Vector2Int(1, 1),
        new Vector2Int(2, 2),
    };

    [MenuItem("Tools/Phase 0/Setup Scene (4x4 Grid)")]
    public static void SetupScene()
    {
        // If current scene is dirty, ask user first.
        if (SceneManager.GetActiveScene().isDirty)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Phase 0 Setup",
                "Current scene has unsaved changes.\n\nProceed and modify the scene?",
                "Proceed",
                "Cancel"
            );
            if (!proceed) return;
        }

        EnsureGeneratedAssets();

        EnsureSortingLayers(new[]
        {
            "Floor",
            "Grid",
            "Piece",
            "Ghost",
            "FX"
        });

        // Remove old root if present (clean re-run).
        var existingRoot = GameObject.Find(RootName);
        if (existingRoot != null)
        {
            Object.DestroyImmediate(existingRoot);
        }

        var root = new GameObject(RootName);

        // Camera (2D top-down, orthographic)
        // Reuse existing MainCamera if present to avoid duplicate AudioListeners from the template scene.
        Camera existingMain = null;
        var existingMainGO = GameObject.FindGameObjectWithTag("MainCamera");
        if (existingMainGO != null) existingMain = existingMainGO.GetComponent<Camera>();

        GameObject camGO;
        Camera cam;

        if (existingMain != null)
        {
            camGO = existingMain.gameObject;
            cam = existingMain;

            // Move under our root for cleanliness
            camGO.transform.SetParent(root.transform, true);
            camGO.name = "MainCamera";
        }
        else
        {
            camGO = new GameObject("MainCamera");
            camGO.transform.SetParent(root.transform);
            cam = camGO.AddComponent<Camera>();
            camGO.tag = "MainCamera";
        }

        cam.orthographic = true;
        cam.orthographicSize = 4.4f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
        camGO.transform.position = new Vector3(0f, 0f, -10f);

        // Ensure exactly one AudioListener in the scene.
        // If one already exists anywhere, remove extra listeners on this camera.
        var listeners = Object.FindObjectsOfType<AudioListener>(true);
        if (listeners == null || listeners.Length == 0)
        {
            if (camGO.GetComponent<AudioListener>() == null) camGO.AddComponent<AudioListener>();
        }
        else
        {
            var onCam = camGO.GetComponent<AudioListener>();
            if (onCam != null && onCam != listeners[0])
                Object.DestroyImmediate(onCam);
        }

        // Scene config placeholder (future scripts can reference)
        var config = new GameObject("SceneConfig");
        config.transform.SetParent(root.transform);
        config.transform.position = Vector3.zero;

        // Board / grid roots
        var board = new GameObject("BoardRoot");
        board.transform.SetParent(root.transform);
        board.transform.position = Vector3.zero;

        var gridRoot = new GameObject("GridRoot");
        gridRoot.transform.SetParent(board.transform);

        // Visual sprite to use everywhere
        var whiteSprite = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteTexturePath);
        if (whiteSprite == null)
        {
            EditorUtility.DisplayDialog("Phase 0 Setup", "Failed to load generated sprite at:\n" + WhiteTexturePath, "OK");
            return;
        }

        // Compute grid origin so it's centered around GridCenter.
        float total = GridSize * CellSize + (GridSize - 1) * CellGap;
        Vector2 bottomLeft = GridCenter - new Vector2(total, total) * 0.5f + new Vector2(CellSize, CellSize) * 0.5f;

        // Create cells (world-space, no Canvas)
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                var cell = new GameObject($"Cell_{x}_{y}");
                cell.transform.SetParent(gridRoot.transform);
                cell.transform.position = new Vector3(
                    bottomLeft.x + x * (CellSize + CellGap),
                    bottomLeft.y + y * (CellSize + CellGap),
                    0f
                );

                var sr = cell.AddComponent<SpriteRenderer>();
                sr.sprite = whiteSprite;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = new Vector2(CellSize, CellSize);
                sr.color = new Color(0.16f, 0.17f, 0.20f, 1f);
                sr.sortingLayerName = "Grid";
                sr.sortingOrder = 0;

                // Light border effect by adding a child outline (cheap & readable)
                var border = new GameObject("Border");
                border.transform.SetParent(cell.transform);
                border.transform.localPosition = Vector3.zero;

                var borderSR = border.AddComponent<SpriteRenderer>();
                borderSR.sprite = whiteSprite;
                borderSR.drawMode = SpriteDrawMode.Sliced;
                borderSR.size = new Vector2(CellSize + 0.06f, CellSize + 0.06f);
                borderSR.color = new Color(0f, 0f, 0f, 0.25f);
                borderSR.sortingLayerName = "Grid";
                borderSR.sortingOrder = -1; // behind the cell

                // Mark blocked cells
                if (DefaultBlockedCells.Any(v => v.x == x && v.y == y))
                {
                    cell.name += "_BLOCKED";
                    sr.color = new Color(0.25f, 0.10f, 0.12f, 1f);
                }
            }
        }

        // Dummy occupied piece (as requested: 1 dummy piece on board)
        var dummy = new GameObject("DummyPiece_Occupied");
        dummy.transform.SetParent(board.transform);

        // Place dummy at a safe spot: (0,3) top-left by default
        var dummyPos = new Vector2Int(0, GridSize - 1);
        dummy.transform.position = new Vector3(
            bottomLeft.x + dummyPos.x * (CellSize + CellGap),
            bottomLeft.y + dummyPos.y * (CellSize + CellGap),
            0f
        );

        var dummySR = dummy.AddComponent<SpriteRenderer>();
        dummySR.sprite = whiteSprite;
        dummySR.drawMode = SpriteDrawMode.Sliced;
        dummySR.size = new Vector2(CellSize, CellSize);
        dummySR.color = new Color(0.35f, 0.36f, 0.40f, 1f);
        dummySR.sortingLayerName = "Piece";
        dummySR.sortingOrder = 10;

        // Active L piece placeholder in a holding area (outside grid but on-screen)
        var holding = new GameObject("HoldingArea");
        holding.transform.SetParent(root.transform);
        holding.transform.position = new Vector3(0f, bottomLeft.y - 2.2f, 0f);

        var piece = new GameObject("ActivePiece_L");
        piece.transform.SetParent(holding.transform);
        piece.transform.localPosition = Vector3.zero;

        // L-shape visual as 3 tiles (placeholder). Replace with Spine later.
        // Rotation / snapping logic will be implemented in runtime scripts later.
        CreateLTiles(piece.transform, whiteSprite);

        // Ghost placeholder (hidden by default)
        var ghost = new GameObject("GhostPreview");
        ghost.transform.SetParent(root.transform);
        ghost.transform.position = new Vector3(0f, 0f, 0f);

        var ghostSR = ghost.AddComponent<SpriteRenderer>();
        ghostSR.sprite = whiteSprite;
        ghostSR.drawMode = SpriteDrawMode.Sliced;
        ghostSR.size = new Vector2(CellSize, CellSize);
        ghostSR.color = new Color(1f, 0.25f, 0.35f, 0.0f); // invisible initially
        ghostSR.sortingLayerName = "Ghost";
        ghostSR.sortingOrder = 50;

        // Helper gizmos root (optional)
        var gizmos = new GameObject("Gizmos");
        gizmos.transform.SetParent(root.transform);

        // Select root for convenience
        Selection.activeGameObject = root;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog(
            "Phase 0 Setup",
            "Scene scaffold created:\n" +
            "- 4x4 world-space grid (no Canvas)\n" +
            "- Orthographic camera (2D)\n" +
            "- 2 blocked cells + 1 dummy occupied piece\n" +
            "- Active L piece placeholder in holding area\n\n" +
            "Next: add runtime scripts for core logic + direct manipulation + tween feel.",
            "OK"
        );
    }

    private static void CreateLTiles(Transform parent, Sprite sprite)
    {
        // L shape in local space:
        // [X][ ]
        // [X][ ]
        // [X][X]
        // using 4 tiles
        Vector2[] localCells =
        {
            new Vector2(0, 2),
            new Vector2(0, 1),
            new Vector2(0, 0),
            new Vector2(1, 0),
        };

        for (int i = 0; i < localCells.Length; i++)
        {
            var tile = new GameObject($"L_Tile_{i}");
            tile.transform.SetParent(parent);
            tile.transform.localPosition = new Vector3(
                localCells[i].x * (CellSize + CellGap),
                localCells[i].y * (CellSize + CellGap),
                0f
            );

            var sr = tile.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(CellSize, CellSize);
            sr.color = new Color(0.92f, 0.93f, 0.96f, 1f);
            sr.sortingLayerName = "Piece";
            sr.sortingOrder = 100;
        }

        // Label (optional) - simple child for later attachment of Spine object
        var spineAnchor = new GameObject("SpineAnchor");
        spineAnchor.transform.SetParent(parent);
        spineAnchor.transform.localPosition = new Vector3(0f, 0.8f, 0f);
    }

    private static void EnsureGeneratedAssets()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Phase0"))
            AssetDatabase.CreateFolder("Assets", "Phase0");

        if (!AssetDatabase.IsValidFolder(GeneratedFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Phase0"))
                AssetDatabase.CreateFolder("Assets", "Phase0");

            AssetDatabase.CreateFolder("Assets/Phase0", "Generated");
        }

        if (!File.Exists(WhiteTexturePath))
        {
            // Create a simple white texture file and import it as Sprite.
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(Color.white, 32 * 32).ToArray();
            tex.SetPixels(pixels);
            tex.Apply();

            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(WhiteTexturePath, png);
            AssetDatabase.ImportAsset(WhiteTexturePath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(WhiteTexturePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 32;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }

    private static void EnsureSortingLayers(string[] needed)
    {
        // Sorting Layers are stored in TagManager.asset.
        // We'll add missing ones, but never delete/rename existing layers.
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var sortingLayersProp = tagManager.FindProperty("m_SortingLayers");
        if (sortingLayersProp == null) return;

        bool changed = false;

        for (int i = 0; i < needed.Length; i++)
        {
            if (HasSortingLayer(sortingLayersProp, needed[i])) continue;

            sortingLayersProp.InsertArrayElementAtIndex(sortingLayersProp.arraySize);
            var sp = sortingLayersProp.GetArrayElementAtIndex(sortingLayersProp.arraySize - 1);

            // Each sorting layer has: name, uniqueID, locked
            sp.FindPropertyRelative("name").stringValue = needed[i];
            sp.FindPropertyRelative("uniqueID").intValue = Random.Range(int.MinValue, int.MaxValue);
            sp.FindPropertyRelative("locked").boolValue = false;

            changed = true;
        }

        if (changed)
        {
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }

    private static bool HasSortingLayer(SerializedProperty sortingLayersProp, string name)
    {
        for (int i = 0; i < sortingLayersProp.arraySize; i++)
        {
            var sp = sortingLayersProp.GetArrayElementAtIndex(i);
            var n = sp.FindPropertyRelative("name").stringValue;
            if (n == name) return true;
        }
        return false;
    }
}
