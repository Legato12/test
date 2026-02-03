// Phase0_SuperFix2_Apply_20260203.cs
// Unity menu: Tools/Phase0/SuperFix #2 Apply (Shadow+Impact+Tilt)
//
// What it patches (Milestone A feel + missing juice):
// - Shadow: ensures it's visible (sorting), and offsets it by (10,-10) px while dragging.
// - Drag tilt: small 2–3° lean based on drag velocity (smoothed).
// - Spine Impact: plays "Impact" on track 1 on valid drop so Idle (track 0) doesn't overwrite it.
//
// Notes:
// - All new knobs are configurable as serialized fields inside Phase0GameFeelFX (injected block).
// - Safe to delete this file after applying (it only patches other scripts/assets).

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Phase0 {
    public static class Phase0_SuperFix2_Apply_20260203 {
        private const string InjectMarker = "SUPERFIX2_JUICE_INJECTED_20260203";
        private const string CallMarker = "SUPERFIX2_CALL_20260203";

        [MenuItem("Tools/Phase0/SuperFix #2 Apply (Shadow+Impact+Tilt)")]
        public static void Apply() {
            var patched = PatchGameFeelFxScript();
            PatchConfigAssets(); // best-effort, reflection-based
            CleanupGeneratedDuplicateShadows(); // best-effort

            AssetDatabase.SaveAssets();
            if (patched) {
                // Triggers recompile so injected code becomes active.
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Phase0 SuperFix2] Done. Patched Phase0GameFeelFX: {patched}");
        }

        // -------------------------
        // 1) Patch Phase0GameFeelFX
        // -------------------------
        private static bool PatchGameFeelFxScript() {
            var path = FindScriptPathByClassName("Phase0GameFeelFX");
            if (string.IsNullOrEmpty(path)) {
                Debug.LogError("[Phase0 SuperFix2] Can't find Phase0GameFeelFX.cs in project.");
                return false;
            }

            var text = File.ReadAllText(path);
            if (text.Contains(InjectMarker)) {
                // Still ensure method calls exist (some people might have copy-pasted only the injected block).
                var updated = EnsureSuperFix2Calls(text, path, alreadyInjected: true);
                if (updated != text) {
                    File.WriteAllText(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    Debug.Log($"[Phase0 SuperFix2] Updated call sites in: {path}");
                    return true;
                }

                Debug.Log($"[Phase0 SuperFix2] Phase0GameFeelFX already patched: {path}");
                return false;
            }

            // Insert injected block before the class closing brace.
            if (!TryFindClassBody(text, "Phase0GameFeelFX", out var classBodyOpenBrace, out var classBodyCloseBrace)) {
                Debug.LogError($"[Phase0 SuperFix2] Failed to parse class body in: {path}");
                return false;
            }

            var injected = GetInjectedBlock();
            var textWithBlock = text.Insert(classBodyCloseBrace, injected);

            // Ensure call sites inside existing methods.
            var finalText = EnsureSuperFix2Calls(textWithBlock, path, alreadyInjected: false);

            File.WriteAllText(path, finalText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Debug.Log($"[Phase0 SuperFix2] Patched: {path}");
            return true;
        }

        private static string EnsureSuperFix2Calls(string text, string pathForLogs, bool alreadyInjected) {
            if (!TryFindClassBody(text, "Phase0GameFeelFX", out var classBodyOpenBrace, out var classBodyCloseBrace)) {
                Debug.LogError($"[Phase0 SuperFix2] Failed to parse class body in: {pathForLogs}");
                return text;
            }

            var sliceStart = classBodyOpenBrace;
            var sliceLen = classBodyCloseBrace - classBodyOpenBrace;

            // Update(): SuperFix2_Tick()
            if (!TryInjectVoidCall(ref text, sliceStart, sliceLen, "Update",
                    callLine: $"SuperFix2_Tick(); // {CallMarker}")) {
                Debug.LogWarning("[Phase0 SuperFix2] Can't patch Update() in Phase0GameFeelFX (method not found). Shadow/Tilt will rely on other calls.");
            }

            // SetDragging(bool param): SuperFix2_OnDraggingChanged(param)
            if (!TryInjectVoidCallWithSingleParam(ref text, sliceStart, sliceLen, "SetDragging",
                    paramTypePattern: @"(?:in\s+)?bool",
                    buildCall: p => $"SuperFix2_OnDraggingChanged({p}); // {CallMarker}")) {
                Debug.LogWarning("[Phase0 SuperFix2] Can't patch SetDragging(bool) in Phase0GameFeelFX (method not found).");
            }

            // UpdateKinematics(Vector3 param): SuperFix2_OnKinematics(param)
            if (!TryInjectVoidCallWithSingleParam(ref text, sliceStart, sliceLen, "UpdateKinematics",
                    paramTypePattern: @"(?:in\s+)?(?:UnityEngine\.)?Vector3",
                    buildCall: p => $"SuperFix2_OnKinematics({p}); // {CallMarker}")) {
                Debug.LogWarning("[Phase0 SuperFix2] Can't patch UpdateKinematics(Vector3) in Phase0GameFeelFX (method not found).");
            }

            // OnDropValid(): SuperFix2_OnDropValid()
            if (!TryInjectVoidCall(ref text, sliceStart, sliceLen, "OnDropValid",
                    callLine: $"SuperFix2_OnDropValid(); // {CallMarker}")) {
                Debug.LogWarning("[Phase0 SuperFix2] Can't patch OnDropValid() in Phase0GameFeelFX (method not found). Impact won't play.");
            }

            return text;
        }

        private static bool TryInjectVoidCall(ref string text, int searchStart, int searchLen, string methodName, string callLine) {
            if (text.Contains(callLine)) return true;

            var rx = new Regex($@"(?m)^[ \t]*(?:public|private|protected|internal|static|virtual|override|sealed|new|async|extern|partial|\s)*\s*void\s+{Regex.Escape(methodName)}\s*\(\s*\)");
            var m = rx.Match(text, searchStart, searchLen);
            if (!m.Success) return false;

            var braceIndex = text.IndexOf('{', m.Index + m.Length);
            if (braceIndex < 0) return false;

            // Insert right after opening brace.
            var indent = DetectIndentAfterBrace(text, braceIndex);
            text = text.Insert(braceIndex + 1, "\n" + indent + callLine + "\n");
            return true;
        }

        private static bool TryInjectVoidCallWithSingleParam(ref string text, int searchStart, int searchLen, string methodName, string paramTypePattern, Func<string, string> buildCall) {
            // If any call marker already exists inside method, skip.
            if (text.Contains($"{methodName}(") && text.Contains(buildCall("___"))) {
                // Not reliable; ignore.
            }

            var rx = new Regex($@"(?m)^[ \t]*(?:public|private|protected|internal|static|virtual|override|sealed|new|async|extern|partial|\s)*\s*void\s+{Regex.Escape(methodName)}\s*\((?<params>[^\)]*)\)");
            var m = rx.Match(text, searchStart, searchLen);
            if (!m.Success) return false;

            var paramsText = m.Groups["params"].Value;
            var rxParam = new Regex($@"\b{paramTypePattern}\s+(?<p>[A-Za-z_][A-Za-z0-9_]*)\b");
            var pm = rxParam.Match(paramsText);
            var paramName = pm.Success ? pm.Groups["p"].Value : null;
            if (string.IsNullOrEmpty(paramName)) {
                // Best-effort fallback
                paramName = "value";
            }

            var callLine = buildCall(paramName);
            if (text.Contains(callLine)) return true;

            var braceIndex = text.IndexOf('{', m.Index + m.Length);
            if (braceIndex < 0) return false;

            var indent = DetectIndentAfterBrace(text, braceIndex);
            text = text.Insert(braceIndex + 1, "\n" + indent + callLine + "\n");
            return true;
        }

        private static string DetectIndentAfterBrace(string text, int braceIndex) {
            // Try to match indentation of method body: indentation of brace line + 4 spaces.
            var lineStart = text.LastIndexOf('\n', Mathf.Max(0, braceIndex - 1));
            if (lineStart < 0) lineStart = 0;
            var i = lineStart;
            while (i < text.Length && (text[i] == '\n' || text[i] == '\r')) i++;

            var ws = 0;
            while (i + ws < text.Length && (text[i + ws] == ' ' || text[i + ws] == '\t')) ws++;

            return text.Substring(i, ws) + "    ";
        }

        private static bool TryFindClassBody(string text, string className, out int openBraceIndex, out int closeBraceIndex) {
            openBraceIndex = -1;
            closeBraceIndex = -1;

            var classIdx = text.IndexOf("class " + className, StringComparison.Ordinal);
            if (classIdx < 0) return false;

            openBraceIndex = text.IndexOf('{', classIdx);
            if (openBraceIndex < 0) return false;

            closeBraceIndex = FindMatchingBrace(text, openBraceIndex);
            return closeBraceIndex > openBraceIndex;
        }

        private static int FindMatchingBrace(string text, int openBraceIndex) {
            var depth = 0;
            for (int i = openBraceIndex; i < text.Length; i++) {
                var c = text[i];
                if (c == '{') depth++;
                else if (c == '}') {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static string FindScriptPathByClassName(string className) {
            // First: direct script-name search.
            var guids = AssetDatabase.FindAssets($"{className} t:Script");
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                try {
                    var txt = File.ReadAllText(path);
                    if (txt.Contains("class " + className)) return path;
                } catch {
                    // ignore
                }
            }

            // Fallback: brute scan scripts (bounded).
            var allScriptGuids = AssetDatabase.FindAssets("t:Script");
            foreach (var guid in allScriptGuids.Take(2000)) { // safety cap
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                try {
                    var txt = File.ReadAllText(path);
                    if (txt.Contains("class " + className)) return path;
                } catch {
                    // ignore
                }
            }

            return null;
        }

        private static string GetInjectedBlock() {
            // Injected directly into Phase0GameFeelFX class.
            // IMPORTANT: no external dependencies; resolves refs at runtime (visualRoot / skeletonAnimation etc) via reflection + hierarchy search.
            var injected = @"

        // =====================================================================
        // {InjectMarker}
        // Adds: Shadow offset, Drag tilt, Spine Impact animation (track 1).
        // All knobs are serialized (configurable) and live on Phase0GameFeelFX.
        // =====================================================================

        [Header(""SuperFix2: Shadow"")]
        [SerializeField] private bool sf2_enableShadow = true;
        [SerializeField] private Vector2 sf2_shadowIdleOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 sf2_shadowPickupOffsetPx = new Vector2(10f, -10f);
        [SerializeField, Range(0f, 1f)] private float sf2_shadowIdleAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float sf2_shadowPickupAlpha = 0.22f;
        [SerializeField] private Vector2 sf2_shadowIdleScale = Vector2.one;
        [SerializeField] private Vector2 sf2_shadowPickupScale = new Vector2(0.92f, 0.92f);
        [Tooltip(""Sorting order relative to the main (Spine) renderer. -1 => behind the cat."")]
        [SerializeField] private int sf2_shadowSortingOrderOffset = -1;

        [Header(""SuperFix2: Drag Tilt"")]
        [SerializeField] private bool sf2_enableDragTilt = true;
        [SerializeField, Range(0f, 15f)] private float sf2_dragTiltMaxDegrees = 3f;
        [SerializeField, Range(0f, 60f)] private float sf2_dragTiltSmoothing = 18f;
        [SerializeField] private float sf2_dragTiltSpeedForMax = 6f;

        [Header(""SuperFix2: Spine Impact (valid drop)"")]
        [SerializeField] private bool sf2_enableSpineImpact = true;
        [SerializeField] private string sf2_impactAnimationName = ""Impact"";
        [SerializeField] private int sf2_impactTrackIndex = 1;
        [SerializeField] private float sf2_impactDistanceForMax = 2.5f;
        [SerializeField, Range(0f, 1f)] private float sf2_impactMinAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float sf2_impactMaxAlpha = 1f;
        [SerializeField] private float sf2_impactMixDuration = 0.06f;

        // ---- cached runtime ----
        private bool sf2_isDragging;
        private Vector3 sf2_dragStartWorld;
        private Vector3 sf2_prevWorld;
        private Vector3 sf2_velWorld;
        private float sf2_tiltDeg;

        private Transform sf2_visualRoot;
        private Quaternion sf2_visualBaseLocalRot;
        private bool sf2_visualBaseRotCached;

        private SpriteRenderer sf2_shadowSR;
        private Transform sf2_shadowT;
        private Vector3 sf2_shadowBaseLocalPos;
        private Vector3 sf2_shadowBaseLocalScale;
        private Color sf2_shadowBaseColor;

        private Renderer sf2_mainRenderer;
        private Spine.Unity.SkeletonAnimation sf2_skeleton;
        private bool sf2_refsReady;

        private void SuperFix2_EnsureRefs() {
            if (sf2_refsReady) return;

            // Visual root: try serialized field (reflection) -> named child -> self.
            try {
                var f = GetType().GetField(""visualRoot"", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f != null) sf2_visualRoot = f.GetValue(this) as Transform;
            } catch { /* ignore */ }

            if (sf2_visualRoot == null) {
                var t = transform.Find(""SpineVisualOffset"");
                if (t != null) sf2_visualRoot = t;
            }

            if (sf2_visualRoot == null) sf2_visualRoot = transform;

            if (!sf2_visualBaseRotCached) {
                sf2_visualBaseLocalRot = sf2_visualRoot.localRotation;
                sf2_visualBaseRotCached = true;
            }

            // Skeleton (Spine): try common serialized fields -> hierarchy.
            try {
                var f = GetType().GetField(""skeletonAnimation"", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f != null) sf2_skeleton = f.GetValue(this) as Spine.Unity.SkeletonAnimation;
            } catch { /* ignore */ }

            if (sf2_skeleton == null) {
                try {
                    var f = GetType().GetField(""spine"", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (f != null) sf2_skeleton = f.GetValue(this) as Spine.Unity.SkeletonAnimation;
                } catch { /* ignore */ }
            }

            if (sf2_skeleton == null) sf2_skeleton = GetComponentInChildren<Spine.Unity.SkeletonAnimation>(true);

            // Main renderer: prefer non-sprite renderer (Spine uses MeshRenderer).
            if (sf2_skeleton != null) sf2_mainRenderer = sf2_skeleton.GetComponent<Renderer>();
            if (sf2_mainRenderer == null) {
                var rends = GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends) {
                    if (r == null) continue;
                    if (r is SpriteRenderer) continue;
                    sf2_mainRenderer = r;
                    break;
                }
            }

            // Shadow: pick best SpriteRenderer with ""shadow"" in name or sprite.
            var candidates = GetComponentsInChildren<SpriteRenderer>(true);
            sf2_shadowSR = null;
            var bestScore = int.MinValue;

            foreach (var sr in candidates) {
                if (sr == null) continue;

                var goName = sr.gameObject.name.ToLowerInvariant();
                var spriteName = sr.sprite != null ? sr.sprite.name.ToLowerInvariant() : string.Empty;

                var isShadowLike = goName.Contains(""shadow"") || spriteName.Contains(""shadow"");
                if (!isShadowLike) continue;

                var score = 0;
                if (sr.sprite != null) score += 10;
                if (sr.sprite != null && !spriteName.Contains(""gen"")) score += 5; // prefer artist sprite over generated
                if (goName == ""shadow"") score += 4;
                if (sr.transform.IsChildOf(sf2_visualRoot)) score += 2;
                if (sr.enabled) score += 1;

                if (score > bestScore) {
                    bestScore = score;
                    sf2_shadowSR = sr;
                }
            }

            if (sf2_shadowSR != null) {
                sf2_shadowT = sf2_shadowSR.transform;

                sf2_shadowBaseLocalPos = sf2_shadowT.localPosition;
                sf2_shadowBaseLocalScale = sf2_shadowT.localScale;
                sf2_shadowBaseColor = sf2_shadowSR.color;

                // Disable other duplicate shadows (keeps hierarchy but avoids double-dark blobs).
                foreach (var sr in candidates) {
                    if (sr == null || sr == sf2_shadowSR) continue;

                    var goName = sr.gameObject.name.ToLowerInvariant();
                    var spriteName = sr.sprite != null ? sr.sprite.name.ToLowerInvariant() : string.Empty;
                    var isShadowLike = goName.Contains(""shadow"") || spriteName.Contains(""shadow"");
                    if (!isShadowLike) continue;

                    sr.enabled = false;
                }

                SuperFix2_SyncShadowSorting();
            }

            sf2_refsReady = true;
        }

        private void SuperFix2_SyncShadowSorting() {
            if (sf2_shadowSR == null || sf2_mainRenderer == null) return;

            sf2_shadowSR.sortingLayerID = sf2_mainRenderer.sortingLayerID;
            sf2_shadowSR.sortingOrder = sf2_mainRenderer.sortingOrder + sf2_shadowSortingOrderOffset;
        }

        private Vector3 SuperFix2_PixelsToWorld(Vector2 px) {
            var cam = Camera.main;
            if (cam != null && cam.orthographic) {
                var ppu = Screen.height / (2f * cam.orthographicSize);
                if (ppu > 0.0001f) {
                    return new Vector3(px.x / ppu, px.y / ppu, 0f);
                }
            }

            // Fallback: assume ~100 px per unit.
            return new Vector3(px.x / 100f, px.y / 100f, 0f);
        }

        private void SuperFix2_ApplyShadow() {
            if (!sf2_enableShadow) return;
            if (sf2_shadowSR == null || sf2_shadowT == null) return;

            SuperFix2_SyncShadowSorting();

            var px = sf2_isDragging ? sf2_shadowPickupOffsetPx : sf2_shadowIdleOffsetPx;
            var worldOff = SuperFix2_PixelsToWorld(px);
            var parent = sf2_shadowT.parent;
            var localOff = parent != null ? parent.InverseTransformVector(worldOff) : worldOff;

            sf2_shadowT.localPosition = sf2_shadowBaseLocalPos + localOff;

            var mul = sf2_isDragging ? sf2_shadowPickupScale : sf2_shadowIdleScale;
            sf2_shadowT.localScale = new Vector3(
                sf2_shadowBaseLocalScale.x * mul.x,
                sf2_shadowBaseLocalScale.y * mul.y,
                sf2_shadowBaseLocalScale.z
            );

            var c = sf2_shadowBaseColor;
            c.a = Mathf.Clamp01(sf2_isDragging ? sf2_shadowPickupAlpha : sf2_shadowIdleAlpha);
            sf2_shadowSR.color = c;

            if (!sf2_shadowSR.enabled) sf2_shadowSR.enabled = true;
        }

        private void SuperFix2_ApplyTilt() {
            if (!sf2_enableDragTilt) return;
            if (sf2_visualRoot == null) return;

            var target = 0f;
            if (sf2_isDragging) {
                var speedForMax = Mathf.Max(0.0001f, sf2_dragTiltSpeedForMax);
                var t = Mathf.Clamp01(sf2_velWorld.magnitude / speedForMax);

                var sign = 0f;
                if (Mathf.Abs(sf2_velWorld.x) > 0.0005f) sign = Mathf.Sign(sf2_velWorld.x);

                target = sign * sf2_dragTiltMaxDegrees * t;
            }

            var k = 1f - Mathf.Exp(-sf2_dragTiltSmoothing * Time.unscaledDeltaTime);
            sf2_tiltDeg = Mathf.Lerp(sf2_tiltDeg, target, k);

            if (!sf2_visualBaseRotCached) {
                sf2_visualBaseLocalRot = sf2_visualRoot.localRotation;
                sf2_visualBaseRotCached = true;
            }

            sf2_visualRoot.localRotation = sf2_visualBaseLocalRot * Quaternion.Euler(0f, 0f, sf2_tiltDeg);
        }

        private void SuperFix2_Tick() {
            SuperFix2_EnsureRefs();
            if (!sf2_refsReady) return;

            SuperFix2_ApplyShadow();
            SuperFix2_ApplyTilt();
        }

        private void SuperFix2_OnDraggingChanged(bool dragging) {
            SuperFix2_EnsureRefs();

            sf2_isDragging = dragging;
            if (dragging) {
                sf2_dragStartWorld = transform.position;
                sf2_prevWorld = transform.position;
                sf2_velWorld = Vector3.zero;
            }
        }

        private void SuperFix2_OnKinematics(Vector3 pieceWorldPos) {
            var dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            sf2_velWorld = (pieceWorldPos - sf2_prevWorld) / dt;
            sf2_prevWorld = pieceWorldPos;
        }

        private void SuperFix2_OnDropValid() {
            if (!sf2_enableSpineImpact) return;

            SuperFix2_EnsureRefs();
            if (sf2_skeleton == null) return;

            var dist = Vector3.Distance(sf2_dragStartWorld, transform.position);
            var t = sf2_impactDistanceForMax > 0.0001f ? Mathf.Clamp01(dist / sf2_impactDistanceForMax) : 1f;
            var alpha = Mathf.Lerp(sf2_impactMinAlpha, sf2_impactMaxAlpha, t);

            try {
                var state = sf2_skeleton.AnimationState;
                if (state == null) return;

                var entry = state.SetAnimation(sf2_impactTrackIndex, sf2_impactAnimationName, false);
                if (entry == null) return;

                entry.Alpha = alpha;
                entry.MixDuration = sf2_impactMixDuration;

                entry.Complete += _ => {
                    try {
                        state.SetEmptyAnimation(sf2_impactTrackIndex, 0f);
                    } catch { /* ignore */ }
                };
            } catch { /* ignore */ }
        }
        // ============================ END SUPERFIX2 ===========================

";
            return injected.Replace("{InjectMarker}", InjectMarker);
        }

        // ----------------------------------------
        // 2) Best-effort config asset adjustments
        // ----------------------------------------
        private static void PatchConfigAssets() {
            // GameFeel settings asset: set Idle name if such field exists.
            TryPatchSOAssetsOfType("Phase0GameFeelSettingsSO", so => {
                SetFieldIfExists(so, "idleAnimationName", "Idle");
                SetFieldIfExists(so, "playIdleAnimation", true);
                SetFieldIfExists(so, "pauseSpineWhileDragging", true);
                SetFieldIfExists(so, "dragStopMixDuration", 0.08f);
            });

            // Scene config asset: snap durations, overshoot strength, etc (if present).
            TryPatchSOAssetsOfType("SceneConfigSO", so => {
                SetFieldIfExists(so, "snapDuration", 0.18f);
                SetFieldIfExists(so, "snapSettleDuration", 0.10f);
                SetFieldIfExists(so, "snapSettleOvershootStrength", 1.35f);
                SetFieldIfExists(so, "returnDuration", 0.30f);

                // Easing enums: set to OutBack if PrimeTween.Ease is used.
                TrySetEnumFieldByName(so, "snapEase", "OutBack");
                TrySetEnumFieldByName(so, "returnEase", "OutQuad");
            });
        }

        private static void TryPatchSOAssetsOfType(string typeName, Action<ScriptableObject> patch) {
            var t = FindType(typeName);
            if (t == null) return;

            var guids = AssetDatabase.FindAssets($"t:{t.Name}");
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath(path, t) as ScriptableObject;
                if (so == null) continue;

                patch(so);
                EditorUtility.SetDirty(so);
            }
        }

        private static Type FindType(string typeName) {
            // Try full name first.
            var t = Type.GetType(typeName);
            if (t != null) return t;

            // Try Phase0 namespace.
            t = Type.GetType("Phase0." + typeName);
            if (t != null) return t;

            // Brute scan.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    t = asm.GetTypes().FirstOrDefault(x => x.Name == typeName);
                    if (t != null) return t;
                } catch {
                    // ignore
                }
            }

            return null;
        }

        private static void SetFieldIfExists(ScriptableObject so, string fieldName, object value) {
            var f = so.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f == null) return;

            try {
                if (f.FieldType == typeof(string)) f.SetValue(so, (string)value);
                else if (f.FieldType == typeof(bool)) f.SetValue(so, (bool)value);
                else if (f.FieldType == typeof(float)) f.SetValue(so, Convert.ToSingle(value));
                else if (f.FieldType.IsEnum && value is string s) {
                    var parsed = Enum.Parse(f.FieldType, s);
                    f.SetValue(so, parsed);
                }
            } catch {
                // ignore
            }
        }

        private static void TrySetEnumFieldByName(ScriptableObject so, string fieldName, string enumValueName) {
            var f = so.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f == null || !f.FieldType.IsEnum) return;

            try {
                var parsed = Enum.Parse(f.FieldType, enumValueName);
                f.SetValue(so, parsed);
            } catch {
                // ignore
            }
        }

        // ---------------------------------------------------
        // 3) Cleanup the shadow duplicate created by SuperFix1
        // ---------------------------------------------------
        private static void CleanupGeneratedDuplicateShadows() {
            // SuperFix1 created a generated sprite named "phase0_shadow_ellipse_gen". If both exist, keep the non-gen one.
            var allSR = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true);
            foreach (var sr in allSR) {
                if (sr == null || sr.sprite == null) continue;

                var spriteName = sr.sprite.name.ToLowerInvariant();
                if (!spriteName.Contains("phase0_shadow_ellipse_gen")) continue;

                // If this shadow is under a piece that also has another shadow sprite, disable the generated one.
                var root = sr.transform.root;
                var otherShadow = root.GetComponentsInChildren<SpriteRenderer>(true)
                    .FirstOrDefault(x => x != null && x != sr && x.sprite != null && x.sprite.name != null
                                         && x.sprite.name.ToLowerInvariant().Contains("shadow")
                                         && !x.sprite.name.ToLowerInvariant().Contains("gen"));

                if (otherShadow != null) {
                    sr.gameObject.SetActive(false);
                    EditorUtility.SetDirty(sr.gameObject);
                }
            }
        }
    }
}
#endif
