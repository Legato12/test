using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Phase0
{
    public static class Phase0UpgradeGlobalGrid
    {
        [MenuItem("Tools/Phase0/Upgrade Scene To Global Grid")]
        private static void UpgradeSceneToGlobalGrid()
        {
            var controller = Object.FindObjectOfType<Phase0GameController>();
            if (controller == null)
            {
                Debug.LogError("Phase0 Upgrade: Phase0GameController not found in the open scene.");
                return;
            }

            // Ensure a SceneConfigSO is assigned (controller also auto-finds at runtime, but we set it here for clarity).
            if (controller.sceneConfig == null)
            {
                var guids = AssetDatabase.FindAssets("t:SceneConfigSO");
                if (guids != null && guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    controller.sceneConfig = AssetDatabase.LoadAssetAtPath<SceneConfigSO>(path);
                }
            }

            var cfg = controller.sceneConfig;
            if (cfg != null)
            {
                // Defaults for the global-grid prototype.
                cfg.globalGridWidth = 0;
                cfg.globalGridHeight = 0;

                if (cfg.rugWidth <= 0) cfg.rugWidth = cfg.gridSize;
                if (cfg.rugHeight <= 0) cfg.rugHeight = cfg.gridSize;

                // Compute rugOrigin in global coords from the current scene camera + rug lattice.
                if (controller.mainCamera != null && controller.gridRoot != null)
                {
                    var lattice = new Phase0BoardMapping();
                    if (lattice.TryAutoInitFromGridRoot(controller.gridRoot, cfg.rugWidth, cfg.rugHeight))
                    {
                        // Initialize global grid with computed dimensions
                        int globalW = cfg.globalGridWidth;
                        int globalH = cfg.globalGridHeight;
                        if (globalW <= 0 || globalH <= 0)
                        {
                            float step = Mathf.Max(Mathf.Abs(lattice.cellStep.x), Mathf.Abs(lattice.cellStep.y));
                            step = Mathf.Max(0.0001f, step);

                            float camH = controller.mainCamera.orthographicSize * 2f;
                            float camW = camH * controller.mainCamera.aspect;

                            globalW = Mathf.Max(cfg.rugWidth, Mathf.FloorToInt(camW / step));
                            globalH = Mathf.Max(cfg.rugHeight, Mathf.FloorToInt(camH / step));

                            globalW = Mathf.Max(1, globalW);
                            globalH = Mathf.Max(1, globalH);
                        }

                        var global = new Phase0GlobalGridMapping();
                        global.InitLatticeAligned(controller.mainCamera, globalW, globalH, lattice);

                        // Set rugOriginGlobal equivalent - derive from the rug center position in global coords
                        cfg.useRugOverride = true;
                        cfg.rugOrigin = global.WorldToCellFloor(lattice.cell00World - lattice.cellStep * 0.5f);

                        // Snap the active piece spawn to the nearest global cell center for deterministic start.
                        if (controller.activePieceRoot != null)
                        {
                            var spawnCell = global.WorldToCellRound(controller.activePieceRoot.position);
                            if (!global.IsInside(spawnCell))
                            {
                                // Clamp to global bounds
                                spawnCell.x = Mathf.Clamp(spawnCell.x, 0, globalW - 1);
                                spawnCell.y = Mathf.Clamp(spawnCell.y, 0, globalH - 1);
                            }
                            controller.activePieceRoot.position = global.CellToWorldCenter(spawnCell);
                        }
                    }
                }

                EditorUtility.SetDirty(cfg);
            }

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Phase0 Upgrade: SceneConfig updated for global grid prototype.");
        }
    }
}
