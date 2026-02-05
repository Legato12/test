using System;
using PrimeTween;
using UnityEngine;

#if SPINE_UNITY
using Spine;
using Spine.Unity;
#endif

namespace Phase0
{
    /// View-only "Apple feel" layer:
    /// - Squash/squash on pickup/drop/rotate (with overshoot)
    /// - Optional subtle scale response during dragging based on velocity
    /// - Optional code-driven bones (head/face) to show "brain drives bones"
    public sealed class Phase0GameFeelFX : MonoBehaviour
    {
        [Header("References")]
        public Transform visualRoot;                 // what we scale
        public UnityEngine.Object skeletonAnimation; // assign SkeletonAnimation if using Spine bones
        public Phase0GameFeelSettingsSO settings;
#if SPINE_UNITY
        public SkeletonAnimation outlineSkeletonAnimation;
#endif
        public Transform outlineRoot;
        [SerializeField] private bool debugForceHatch;

        private Vector3 _lastPos;
        private Vector3 _vel;
        private bool _dragging;

        // tween state
        private Vector3 _baseScale;
        private Tween _scaleTween;

        // invalid "no" shake timer
        private float _noShakeT;        private float _noShakeDur;
        private float _noShakeAmpDeg;

#if SPINE_UNITY
        private SkeletonAnimation _sa;
        private Spine.Bone _headBone;
        private Spine.Bone _faceBone;
        private Spine.Bone _earLBone;
        private Spine.Bone _earRBone;
        private Spine.Bone _tailBone;
        private Spine.Bone _mouthBone;
        private float _headRotDeg;
        private float _faceX;
        private float _faceY;
        private SpineState _spineState = SpineState.Idle;
        private float _idleT;
        private bool _loggedMissingHead;
        private bool _loggedMissingFace;
        private bool _loggedMissingEarL;
        private bool _loggedMissingEarR;
        private bool _loggedMissingTail;
        private bool _loggedMissingMouth;
#endif

        private bool _invalidHatchActive;
        private bool _invalidVisualActive;
        private float _hatchScale = -1f;

#if SPINE_UNITY
        private Renderer[] _hatchRenderers;
        private Renderer _outlineCenterRenderer;
        private MaterialPropertyBlock _hatchBlock;
        private Vector3 _lastOutlineCenterWs;
        private bool _hasOutlineCenter;

        private static readonly int HatchStrengthId = Shader.PropertyToID("_HatchStrength");
        private static readonly int HatchColorId = Shader.PropertyToID("_HatchColor");
        private static readonly int HatchScaleId = Shader.PropertyToID("_HatchScale");
        private static readonly int HatchWidthId = Shader.PropertyToID("_HatchWidth");
        private static readonly int HatchAngleId = Shader.PropertyToID("_HatchAngleDeg");
        private static readonly int HatchOpacityId = Shader.PropertyToID("_HatchOpacity");
        private static readonly int HatchUseWorldSpaceId = Shader.PropertyToID("_HatchUseWorldSpace");
        private static readonly int HatchScrollVelocityId = Shader.PropertyToID("_HatchScrollVelocity");
        private static readonly int OutlineEnabledId = Shader.PropertyToID("_OutlineEnabled");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThicknessPx");
        private static readonly int OutlineCenterId = Shader.PropertyToID("_OutlineCenterWS");
#endif
        private bool _loggedForceHatch;

#if SPINE_UNITY
        private enum SpineState
        {
            Idle,
            Dragging
        }
#endif

        private void Awake()
        {
            if (visualRoot == null) visualRoot = transform;
            _baseScale = visualRoot.localScale;
            _lastPos = visualRoot.position;

#if SPINE_UNITY
            _sa = ResolveSkeletonAnimation();
            BindSkeletonEvents();
            TryBindBones(logSuccess: false);
            ApplySpineState(SpineState.Idle, force: true);
#endif
            ApplyHatchOverlay();
        }

        private void Start()
        {
        }

        private void OnEnable()
        {
            if (visualRoot == null) visualRoot = transform;
            _baseScale = visualRoot.localScale;
            _lastPos = visualRoot.position;

#if SPINE_UNITY
            _sa = ResolveSkeletonAnimation();
            BindSkeletonEvents();
            TryBindBones(logSuccess: false);
            ApplySpineState(SpineState.Idle, force: true);
#endif
            ApplyHatchOverlay();
        }

        private void OnDisable()
        {
#if SPINE_UNITY
            UnbindSkeletonEvents();
#endif
        }

        public void SetDragging(bool dragging)
        {
            SuperFix2_OnDraggingChanged(dragging); // SUPERFIX2_CALL_20260203

            _dragging = dragging;
#if SPINE_UNITY
            ApplySpineState(dragging ? SpineState.Dragging : SpineState.Idle);
#endif
        }

        public void SetInvalidHatch(bool enabled)
        {
            if (settings != null && !settings.enableInvalidHatch) enabled = false;
            if (_invalidHatchActive == enabled) return;
            _invalidHatchActive = enabled;
            ApplyHatchOverlay();
        }

        public void SetInvalidVisual(bool invalid)
        {
            bool previousVisual = _invalidVisualActive;
            bool previousHatch = _invalidHatchActive;
            _invalidVisualActive = invalid;

            bool hatchEnabled = invalid;
            if (settings != null && !settings.enableInvalidHatch) hatchEnabled = false;
            _invalidHatchActive = hatchEnabled;

            if (previousVisual != _invalidVisualActive || previousHatch != _invalidHatchActive)
            {
                ApplyHatchOverlay();
            }
#if SPINE_UNITY
            if (_invalidVisualActive)
            {
                UpdateOutlineCenterIfNeeded(force: true);
            }
#endif
        }

        public void SetHatchScaleForCellSize(float cellSize)
        {
            float stripesPerCell = settings != null ? settings.stripesPerCell : 8f;
            float next = cellSize > 0.0001f ? (stripesPerCell / cellSize) : 4f;
            SetHatchScale(next);
        }

        private void SetHatchScale(float worldScale)
        {
            float next = Mathf.Max(0.01f, worldScale);
            if (Mathf.Abs(next - _hatchScale) <= 0.0001f) return;
            _hatchScale = next;
            ApplyHatchOverlay();
        }

        // Call every frame from controller with piece root position (the thing you move)
        public void UpdateKinematics(Vector3 pieceWorldPos)
        {
            SuperFix2_OnKinematics(pieceWorldPos); // SUPERFIX2_CALL_20260203

            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            _vel = (pieceWorldPos - _lastPos) / dt;
            _lastPos = pieceWorldPos;
        }

        public void OnPickup()
        {
            if (!Validate()) return;
            PlayScaleSpring(new Vector3(settings.pickupScale.x, settings.pickupScale.y, 1f));
        }

        public void OnRotateTap()
        {
            if (!Validate()) return;
            PlayScaleSpring(new Vector3(settings.rotateTapScale.x, settings.rotateTapScale.y, 1f));
        }

        public void OnDropValid()
        {
            SuperFix2_OnDropValid(); // SUPERFIX2_CALL_20260203

            if (!Validate()) return;
            Impact(settings.dropValidImpactScale);
        }

        public void OnDropInvalid()
        {
            if (!Validate()) return;
            SuperFix2_OnDropInvalid(); // SUPERFIX2_CALL_20260203
            TriggerNoShake();
        }

        public void PlayReleaseInvalid(Transform pieceRoot, Renderer[] cachedRenderers)
        {
            OnDropInvalid();
        }

        private void Impact(Vector2 impact)
        {
            Vector3 impactScale = new Vector3(impact.x, impact.y, 1f);
            Vector3 overshootScale = new Vector3(settings.overshootScale.x, settings.overshootScale.y, 1f);
            _scaleTween.Stop();
            var sequence = PrimeTween.Sequence.Create();
            bool chained = false;
            Vector3 lastTarget = visualRoot.localScale;

            if (!IsSameScale(lastTarget, impactScale))
            {
                sequence.Chain(Tween.Scale(visualRoot, impactScale, 0.08f, Ease.OutExpo));
                chained = true;
                lastTarget = impactScale;
            }

            if (!IsSameScale(lastTarget, overshootScale))
            {
                sequence.Chain(Tween.Scale(visualRoot, overshootScale, 0.10f, Ease.OutBack));
                chained = true;
            }

            if (!chained) return;
        }

        private void QueueReturnToBase()
        {
            _scaleTween.Stop();
            if (IsSameScale(visualRoot.localScale, _baseScale)) return;
            PrimeTween.Sequence.Create()
                .Chain(Tween.Scale(visualRoot, _baseScale, settings.returnDuration, Ease.OutBack));
        }

        private bool Validate() => visualRoot != null && settings != null;

        private void Update()
        {
            SuperFix2_Tick(); // SUPERFIX2_CALL_20260203

            TickDragScale();
            TickNoShake();
#if SPINE_UNITY
            TickIdle();
            TickBoneFollow();
#endif
#if SPINE_UNITY
            TickOutlineCenter();
#endif
        }

#if SPINE_UNITY
        private void TickIdle()
        {
            if (_spineState != SpineState.Idle) return;
            _idleT += Time.deltaTime;
        }
#endif

        private void TickDragScale()
        {
            if (!Validate() || !_dragging) return;
            if (settings.dragSpeedScaleAmount <= 0f) return;

            float speed = _vel.magnitude;
            float k = Mathf.Clamp01(speed / Mathf.Max(settings.dragSpeedForMax, 0.01f));
            float amt = settings.dragSpeedScaleAmount * k;

            Vector3 target = new Vector3(_baseScale.x * (1f + amt), _baseScale.y * (1f - amt), _baseScale.z);

            if (!_scaleTween.isAlive)
                visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, target, 0.25f);
        }

        private void PlayScaleSpring(Vector3 peakScale)
        {
            _scaleTween.Stop();
            var sequence = PrimeTween.Sequence.Create();
            bool chained = false;
            Vector3 lastTarget = visualRoot.localScale;

            if (!IsSameScale(lastTarget, peakScale))
            {
                sequence.Chain(Tween.Scale(visualRoot, peakScale, settings.quickScaleDuration, Ease.OutBack));
                chained = true;
                lastTarget = peakScale;
            }

            if (!IsSameScale(lastTarget, _baseScale))
            {
                sequence.Chain(Tween.Scale(visualRoot, _baseScale, settings.returnDuration, Ease.OutBack));
                chained = true;
            }

            if (!chained) return;
        }

        private void TriggerNoShake()
        {
            if (settings == null || !settings.enableBoneFollow)
            {
                return;
            }

            _noShakeT = 0f;
            _noShakeDur = Mathf.Max(0.01f, settings.headNoShakeDuration);
            _noShakeAmpDeg = settings.headNoShakeDegrees;
        }

        private void TickNoShake()
        {
            if (_noShakeDur <= 0f) return;
            if (_noShakeT >= _noShakeDur) return;
            _noShakeT += Time.deltaTime;
        }

#if SPINE_UNITY
        private void TickBoneFollow()
        {
            if (!Validate() || !settings.enableBoneFollow)
            {
                return;
            }
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null || _sa.Skeleton == null)
            {
                return;
            }

            if (_headBone == null && _faceBone == null) TryBindBones(logSuccess: false);

            // compute targets; actual bone write happens in Spine UpdateLocal hook
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            float velScale = Mathf.Max(settings.boneVelocityScale, 0.01f);
            float vx = _vel.x * velScale;
            float vy = _vel.y * velScale;
            float kx = Mathf.Clamp(vx, -1f, 1f);
            float ky = Mathf.Clamp(vy, -1f, 1f);

            float desiredHead = Mathf.Clamp((-kx) * settings.headTiltDegrees,
                -settings.headTiltDegrees, settings.headTiltDegrees);

            if (_spineState == SpineState.Idle && settings.enableIdleBreathing)
            {
                float idle = Mathf.Sin(_idleT * settings.idleBreathSpeed * Mathf.PI * 2f);
                desiredHead += idle * settings.idleHeadBreathDegrees;
            }

            float noShake = 0f;
            if (_noShakeT < _noShakeDur)
            {
                float u = Mathf.Clamp01(_noShakeT / _noShakeDur);
                noShake = Mathf.Sin(u * Mathf.PI * 4f) * _noShakeAmpDeg * (1f - u);
            }

            float a = 1f - Mathf.Exp(-settings.headFollowStiffness * dt);
            _headRotDeg = Mathf.Lerp(_headRotDeg, desiredHead + noShake, a);

            float desiredFaceX = Mathf.Clamp(kx * settings.faceBob,
                settings.faceOffsetXMinMax.x, settings.faceOffsetXMinMax.y);
            float desiredFaceY = Mathf.Clamp(ky * settings.faceBob,
                settings.faceOffsetYMinMax.x, settings.faceOffsetYMinMax.y);

            if (_spineState == SpineState.Idle && settings.enableIdleBreathing)
            {
                float idle = Mathf.Sin(_idleT * settings.idleBreathSpeed * Mathf.PI * 2f);
                desiredFaceX += idle * settings.idleFaceBreath;
                desiredFaceY += idle * settings.idleFaceBreath;
            }

            float b = 1f - Mathf.Exp(-settings.faceFollowStiffness * dt);
            _faceX = Mathf.Lerp(_faceX, desiredFaceX, b);
            _faceY = Mathf.Lerp(_faceY, desiredFaceY, b);
        }
#endif

#if SPINE_UNITY
        private void BindSkeletonEvents()
        {
            if (_sa == null) return;
            _sa.UpdateWorld -= OnSpineUpdateWorld;
            _sa.UpdateWorld += OnSpineUpdateWorld;
        }

        private void UnbindSkeletonEvents()
        {
            if (_sa == null) return;
            _sa.UpdateWorld -= OnSpineUpdateWorld;
        }

        private void OnSpineUpdateWorld(ISkeletonAnimation anim)
        {
            if (!Validate() || !settings.enableBoneFollow)
            {
                return;
            }
            if (_headBone == null && _faceBone == null && _earLBone == null && _earRBone == null && _tailBone == null && _mouthBone == null)
                TryBindBones(logSuccess: false);

            if (_headBone != null)
            {
                _headBone.Rotation = _headRotDeg;
            }

            if (_faceBone != null)
            {
                _faceBone.X = _faceX;
                _faceBone.Y = _faceY;
            }
        }

        private Spine.Bone FindBoneWithFallback(string primaryName, string[] fallbackNames)
        {
            if (_sa == null || _sa.Skeleton == null) return null;

            // Try primary name first
            if (!string.IsNullOrEmpty(primaryName))
            {
                var bone = _sa.Skeleton.FindBone(primaryName);
                if (bone != null) return bone;
            }

            // Try fallback names
            if (fallbackNames != null)
            {
                foreach (var name in fallbackNames)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        var bone = _sa.Skeleton.FindBone(name);
                        if (bone != null) return bone;
                    }
                }
            }

            return null;
        }

        private void TryBindBones(bool logSuccess)
        {
            if (_sa == null || _sa.Skeleton == null || settings == null)
            {
                return;
            }

            // Head bone with fallbacks
            string[] headFallbacks = { "head", "Head", "HEAD", "root", "Root", "neck", "Neck" };
            _headBone = FindBoneWithFallback(settings.headBoneName, headFallbacks);

            // Face bone with fallbacks
            string[] faceFallbacks = { "face", "Face", "FACE", "mouth", "Mouth", "eyes", "Eyes" };
            _faceBone = FindBoneWithFallback(settings.faceBoneName, faceFallbacks);

            // Other bones
            if (!string.IsNullOrEmpty(settings.earLBoneName))
            {
                _earLBone = _sa.Skeleton.FindBone(settings.earLBoneName);
            }

            if (!string.IsNullOrEmpty(settings.earRBoneName))
            {
                _earRBone = _sa.Skeleton.FindBone(settings.earRBoneName);
            }

            if (!string.IsNullOrEmpty(settings.tailBoneName))
            {
                _tailBone = _sa.Skeleton.FindBone(settings.tailBoneName);
            }

            if (!string.IsNullOrEmpty(settings.mouthBoneName))
            {
                _mouthBone = _sa.Skeleton.FindBone(settings.mouthBoneName);
            }

            if (settings.logMissingBones)
            {
                if (_headBone == null && !_loggedMissingHead && !string.IsNullOrEmpty(settings.headBoneName))
                {
                    Debug.LogWarning($"Phase0GameFeelFX: Missing Spine bone '{settings.headBoneName}' on '{name}'.");
                    _loggedMissingHead = true;
                }

                if (_faceBone == null && !_loggedMissingFace && !string.IsNullOrEmpty(settings.faceBoneName))
                {
                    Debug.LogWarning($"Phase0GameFeelFX: Missing Spine bone '{settings.faceBoneName}' on '{name}'.");
                    _loggedMissingFace = true;
                }

                if (_earLBone == null && !_loggedMissingEarL && !string.IsNullOrEmpty(settings.earLBoneName))
                {
                    Debug.LogWarning($"Phase0GameFeelFX: Missing Spine bone '{settings.earLBoneName}' on '{name}'.");
                    _loggedMissingEarL = true;
                }

                if (_earRBone == null && !_loggedMissingEarR && !string.IsNullOrEmpty(settings.earRBoneName))
                {
                    Debug.LogWarning($"Phase0GameFeelFX: Missing Spine bone '{settings.earRBoneName}' on '{name}'.");
                    _loggedMissingEarR = true;
                }

                if (_tailBone == null && !_loggedMissingTail && !string.IsNullOrEmpty(settings.tailBoneName))
                {
                    Debug.LogWarning($"Phase0GameFeelFX: Missing Spine bone '{settings.tailBoneName}' on '{name}'.");
                    _loggedMissingTail = true;
                }

                if (_mouthBone == null && !_loggedMissingMouth && !string.IsNullOrEmpty(settings.mouthBoneName))
                {
                    Debug.LogWarning($"Phase0GameFeelFX: Missing Spine bone '{settings.mouthBoneName}' on '{name}'.");
                    _loggedMissingMouth = true;
                }
            }

            if (logSuccess && settings.logBoneBindSuccess)
            {
                if (_headBone != null)
                {
                    Debug.Log($"Phase0GameFeelFX: Bound head bone '{_headBone.Data.Name}' on '{name}'.");
                }

                if (_faceBone != null)
                {
                    Debug.Log($"Phase0GameFeelFX: Bound face bone '{_faceBone.Data.Name}' on '{name}'.");
                }

                if (_earLBone != null)
                {
                    Debug.Log($"Phase0GameFeelFX: Bound ear L bone '{_earLBone.Data.Name}' on '{name}'.");
                }

                if (_earRBone != null)
                {
                    Debug.Log($"Phase0GameFeelFX: Bound ear R bone '{_earRBone.Data.Name}' on '{name}'.");
                }

                if (_tailBone != null)
                {
                    Debug.Log($"Phase0GameFeelFX: Bound tail bone '{_tailBone.Data.Name}' on '{name}'.");
                }

                if (_mouthBone != null)
                {
                    Debug.Log($"Phase0GameFeelFX: Bound mouth bone '{_mouthBone.Data.Name}' on '{name}'.");
                }
            }
        }

        private SkeletonAnimation ResolveSkeletonAnimation()
        {
            if (skeletonAnimation == null) return null;

            if (skeletonAnimation is SkeletonAnimation sa)
            {
                return sa;
            }

            if (skeletonAnimation is GameObject go)
            {
                return go.GetComponent<SkeletonAnimation>();
            }

            if (skeletonAnimation is Component comp)
            {
                return comp.GetComponent<SkeletonAnimation>();
            }

            return null;
        }

        private void ApplySpineState(SpineState state, bool force = false)
        {
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;
            if (!force && _spineState == state) return;

            _spineState = state;
            _idleT = 0f;

            if (_sa.AnimationState != null)
            {
                if (state == SpineState.Dragging)
                {
                    if (settings != null && settings.pauseSpineWhileDragging)
                    {
                        float mix = settings.dragStopMixDuration;
                        _sa.AnimationState.SetEmptyAnimation(0, Mathf.Max(0f, mix));
                    }
                }
                else
                {
                    if (settings != null && settings.playIdleAnimation && !string.IsNullOrEmpty(settings.idleAnimationName))
                    {
                        if (_sa.AnimationState.Data.SkeletonData.FindAnimation(settings.idleAnimationName) != null)
                        {
                            _sa.AnimationState.SetAnimation(0, settings.idleAnimationName, true);
                        }
                        else if (settings.logMissingBones)
                        {
                            Debug.LogWarning($"Phase0GameFeelFX: Idle animation '{settings.idleAnimationName}' not found on '{name}'.");
                        }
                    }
                }
            }

            TryBindBones(logSuccess: true);
        }
#endif

        private void ApplyHatchOverlay()
        {
#if SPINE_UNITY
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;
            if (_hatchRenderers == null || _hatchRenderers.Length == 0) CacheHatchRenderers();
            if (_hatchRenderers == null || _hatchRenderers.Length == 0) return;

            if (_hatchBlock == null) _hatchBlock = new MaterialPropertyBlock();
            float hatchStrength = _invalidHatchActive && settings != null ? settings.hatchStrength : 0f;
            float hatchScale = ResolveHatchScale();
            float useWorldSpace = settings != null && settings.useWorldSpaceHatch ? 1f : 0f;
            Vector2 hatchScrollVelocity = settings != null ? settings.hatchScrollVelocity : Vector2.zero;
            if (hatchStrength <= 0f)
            {
                hatchScrollVelocity = Vector2.zero;
            }

            bool outlineEnabled = _invalidVisualActive && settings != null && (settings.invalidOutlineEnabled || settings.enableInvalidOutline);
            Color outlineColor = settings != null ? settings.outlineColor : Color.red;
            float outlineThickness = settings != null ? Mathf.Max(0f, settings.outlineThicknessPx) : 0f;
            Vector4 outlineCenter = ResolveOutlineCenterVector(outlineEnabled);

            for (int i = 0; i < _hatchRenderers.Length; i++)
            {
                var renderer = _hatchRenderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_hatchBlock);
                _hatchBlock.SetFloat(HatchStrengthId, hatchStrength);
                _hatchBlock.SetColor(HatchColorId, settings != null ? settings.hatchColor : Color.black);
                _hatchBlock.SetFloat(HatchScaleId, hatchScale);
                _hatchBlock.SetFloat(HatchWidthId, settings != null ? settings.hatchWidth : 0.18f);
                _hatchBlock.SetFloat(HatchAngleId, settings != null ? settings.hatchAngleDeg : 45f);
                _hatchBlock.SetFloat(HatchOpacityId, settings != null ? settings.hatchOpacity : 0.8f);
                _hatchBlock.SetFloat(HatchUseWorldSpaceId, useWorldSpace);
                _hatchBlock.SetVector(HatchScrollVelocityId, hatchScrollVelocity);
                _hatchBlock.SetFloat(OutlineEnabledId, outlineEnabled ? 1f : 0f);
                _hatchBlock.SetColor(OutlineColorId, outlineColor);
                _hatchBlock.SetFloat(OutlineThicknessId, outlineThickness);
                _hatchBlock.SetVector(OutlineCenterId, outlineCenter);
                renderer.SetPropertyBlock(_hatchBlock);
            }
#endif
        }

        private float ResolveHatchScale()
        {
            if (_hatchScale > 0.0001f) return _hatchScale;
            if (settings != null && settings.hatchScale > 0.0001f) return settings.hatchScale;
            return 4f;
        }

        private void ApplyOutlineVisual(bool? invalidOverride = null)
        {
            // Outline visuals are now handled by shader overlay.
            // OutlineRoot/OutlineSkeletonAnimation fields are kept for backward-compatible inspector data,
            // but are intentionally unused to avoid disabling SpineAnchor.
        }

#if SPINE_UNITY
        private void TickOutlineCenter()
        {
            if (!_invalidVisualActive) return;
            UpdateOutlineCenterIfNeeded(force: _dragging || !_hasOutlineCenter);
        }

        private void UpdateOutlineCenterIfNeeded(bool force)
        {
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;
            if (_outlineCenterRenderer == null) CacheOutlineCenterRenderer();
            if (_outlineCenterRenderer == null) return;

            Vector3 center = _outlineCenterRenderer.bounds.center;
            if (!force && _hasOutlineCenter && (center - _lastOutlineCenterWs).sqrMagnitude < 0.0001f)
            {
                return;
            }

            _lastOutlineCenterWs = center;
            _hasOutlineCenter = true;
            ApplyOutlineCenterToRenderers(center);
        }

        private Vector4 ResolveOutlineCenterVector(bool outlineEnabled)
        {
            if (!outlineEnabled)
            {
                return Vector4.zero;
            }

            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return Vector4.zero;
            if (_outlineCenterRenderer == null) CacheOutlineCenterRenderer();
            if (_outlineCenterRenderer == null) return Vector4.zero;

            Vector3 center = _outlineCenterRenderer.bounds.center;
            _lastOutlineCenterWs = center;
            _hasOutlineCenter = true;
            return new Vector4(center.x, center.y, center.z, 1f);
        }

        private void ApplyOutlineCenterToRenderers(Vector3 center)
        {
            if (_hatchRenderers == null || _hatchRenderers.Length == 0) CacheHatchRenderers();
            if (_hatchRenderers == null || _hatchRenderers.Length == 0) return;
            if (_hatchBlock == null) _hatchBlock = new MaterialPropertyBlock();

            Vector4 value = new Vector4(center.x, center.y, center.z, 1f);
            for (int i = 0; i < _hatchRenderers.Length; i++)
            {
                var renderer = _hatchRenderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_hatchBlock);
                _hatchBlock.SetVector(OutlineCenterId, value);
                renderer.SetPropertyBlock(_hatchBlock);
            }
        }
#endif

#if SPINE_UNITY
        private void CacheHatchRenderers()
        {
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;
            var renderers = _sa.GetComponents<Renderer>();
            if (renderers == null || renderers.Length == 0) return;

            var filtered = new System.Collections.Generic.List<Renderer>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (r is SpriteRenderer) continue;
                filtered.Add(r);
            }

            _hatchRenderers = filtered.ToArray();
        }

        private void CacheOutlineCenterRenderer()
        {
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;

            _outlineCenterRenderer = _sa.GetComponent<MeshRenderer>();
            if (_outlineCenterRenderer == null)
            {
                _outlineCenterRenderer = _sa.GetComponent<Renderer>();
            }
            if (_outlineCenterRenderer == null)
            {
                _outlineCenterRenderer = _sa.GetComponentInChildren<Renderer>();
            }
        }

        private void LogSpineRenderers()
        {
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;
            var renderers = _sa.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                Debug.Log("Phase0GameFeelFX: No renderers found under Spine object.");
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                var mats = renderer.sharedMaterials;
                int matCount = mats != null ? mats.Length : 0;
                Debug.Log($"Phase0GameFeelFX: Renderer '{renderer.name}' ({renderer.GetType().Name}) mats={matCount}.");
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    bool hasStrength = mat.HasProperty("_HatchStrength");
                    bool hasScale = mat.HasProperty("_HatchScale");
                    Debug.Log($"Phase0GameFeelFX:  - Mat[{m}] '{mat.name}' shader='{mat.shader.name}' hasStrength={hasStrength} hasScale={hasScale}.");
                }
            }
        }

        private void TickDebugForceHatch()
        {
            if (!debugForceHatch) return;
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;
            if (_hatchRenderers == null || _hatchRenderers.Length == 0) CacheHatchRenderers();
            if (_hatchRenderers == null || _hatchRenderers.Length == 0) return;

            if (_hatchBlock == null) _hatchBlock = new MaterialPropertyBlock();
            for (int i = 0; i < _hatchRenderers.Length; i++)
            {
                var renderer = _hatchRenderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_hatchBlock);
                _hatchBlock.SetFloat(HatchStrengthId, 1f);
                _hatchBlock.SetFloat(HatchOpacityId, 1f);
                _hatchBlock.SetFloat(HatchWidthId, 0.25f);
                _hatchBlock.SetFloat(HatchScaleId, 3f);
                _hatchBlock.SetFloat(OutlineEnabledId, 1f);
                _hatchBlock.SetColor(OutlineColorId, Color.red);
                _hatchBlock.SetFloat(OutlineThicknessId, 3f);
                renderer.SetPropertyBlock(_hatchBlock);
            }

            if (!_loggedForceHatch)
            {
                _loggedForceHatch = true;
                string names = string.Join(", ", System.Array.ConvertAll(_hatchRenderers, r => r != null ? r.name : "<null>"));
                Debug.Log($"Phase0GameFeelFX: debugForceHatch active. Applied MPB to: {names}");
            }
        }
#endif

        private void TweenScale(Vector3 to, float duration, Ease ease)
        {
            if (IsSameScale(visualRoot.localScale, to)) return;
            _scaleTween.Stop();
            _scaleTween = Tween.Scale(visualRoot, to, duration, ease);
        }

        private static bool IsSameScale(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude <= 0.000001f;
        }
    

        // =====================================================================
        // SUPERFIX2_JUICE_INJECTED_20260203
        // Adds: Shadow offset, Drag tilt, Spine Impact animation (track 1).
        // All knobs are serialized (configurable) and live on Phase0GameFeelFX.
        // =====================================================================

        [Header("SuperFix2: Shadow")]
        [SerializeField] private bool sf2_enableShadow = true;
        [SerializeField] private Vector2 sf2_shadowIdleOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 sf2_shadowPickupOffsetPx = new Vector2(10f, -10f);
        [SerializeField, Range(0f, 1f)] private float sf2_shadowIdleAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float sf2_shadowPickupAlpha = 0.22f;
        [SerializeField] private Vector2 sf2_shadowIdleScale = Vector2.one;
        [SerializeField] private Vector2 sf2_shadowPickupScale = new Vector2(0.92f, 0.92f);
        [Tooltip("Sorting order relative to the main (Spine) renderer. -1 => behind the cat.")]
        [SerializeField] private int sf2_shadowSortingOrderOffset = -1;

        [Header("SuperFix2: Drag Tilt")]
        [SerializeField] private bool sf2_enableDragTilt = true;
        [SerializeField, Range(0f, 15f)] private float sf2_dragTiltMaxDegrees = 3f;
        [SerializeField, Range(0f, 60f)] private float sf2_dragTiltSmoothing = 18f;
        [SerializeField] private float sf2_dragTiltSpeedForMax = 6f;

        [Header("SuperFix2: Spine Impact (valid drop)")]
        [SerializeField] private bool sf2_enableSpineImpact = true;
        [SerializeField] private string sf2_impactAnimationName = "Impact";
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
#if SPINE_UNITY
        private Spine.Unity.SkeletonAnimation sf2_skeleton;
#endif
        private bool sf2_refsReady;

        private void SuperFix2_EnsureRefs() {
            if (sf2_refsReady) return;

            // Visual root: try serialized field (reflection) -> named child -> self.
            try {
                var f = GetType().GetField("visualRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f != null) sf2_visualRoot = f.GetValue(this) as Transform;
            } catch { /* ignore */ }

            if (sf2_visualRoot == null) {
                var t = transform.Find("SpineVisualOffset");
                if (t != null) sf2_visualRoot = t;
            }

            if (sf2_visualRoot == null) sf2_visualRoot = transform;

            if (!sf2_visualBaseRotCached) {
                sf2_visualBaseLocalRot = sf2_visualRoot.localRotation;
                sf2_visualBaseRotCached = true;
            }

#if SPINE_UNITY
            // Skeleton (Spine): try common serialized fields -> hierarchy.
            try {
                var f = GetType().GetField("skeletonAnimation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f != null) sf2_skeleton = f.GetValue(this) as Spine.Unity.SkeletonAnimation;
            } catch { /* ignore */ }

            if (sf2_skeleton == null) {
                try {
                    var f = GetType().GetField("spine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (f != null) sf2_skeleton = f.GetValue(this) as Spine.Unity.SkeletonAnimation;
                } catch { /* ignore */ }
            }

            if (sf2_skeleton == null) sf2_skeleton = GetComponentInChildren<Spine.Unity.SkeletonAnimation>(true);

            // Main renderer: prefer non-sprite renderer (Spine uses MeshRenderer).
            if (sf2_skeleton != null) sf2_mainRenderer = sf2_skeleton.GetComponent<Renderer>();
#endif
            if (sf2_mainRenderer == null) {
                var rends = GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends) {
                    if (r == null) continue;
                    if (r is SpriteRenderer) continue;
                    sf2_mainRenderer = r;
                    break;
                }
            }

            // Shadow: pick best SpriteRenderer with "shadow" in name or sprite.
            var candidates = GetComponentsInChildren<SpriteRenderer>(true);
            sf2_shadowSR = null;
            var bestScore = int.MinValue;

            foreach (var sr in candidates) {
                if (sr == null) continue;

                var goName = sr.gameObject.name.ToLowerInvariant();
                var spriteName = sr.sprite != null ? sr.sprite.name.ToLowerInvariant() : string.Empty;

                var isShadowLike = goName.Contains("shadow") || spriteName.Contains("shadow");
                if (!isShadowLike) continue;

                var score = 0;
                if (sr.sprite != null) score += 10;
                if (sr.sprite != null && !spriteName.Contains("gen")) score += 5; // prefer artist sprite over generated
                if (goName == "shadow") score += 4;
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
                    var isShadowLike = goName.Contains("shadow") || spriteName.Contains("shadow");
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
            if (!sf2_isDragging && Mathf.Abs(sf2_tiltDeg) <= 0.001f) {
                sf2_visualBaseLocalRot = sf2_visualRoot.localRotation;
                return;
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
            if (!Validate()) return;
            Impact(settings.dropValidImpactScale);
        }

        private void SuperFix2_OnDropInvalid() {
#if SPINE_UNITY
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
#endif
        }
        // ============================ END SUPERFIX2 ===========================

}
}