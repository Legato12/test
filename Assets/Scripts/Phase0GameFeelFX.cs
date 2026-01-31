using System;
using PrimeTween;
using UnityEngine;

using Spine;
using Spine.Unity;

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

        private Vector3 _lastPos;
        private Vector3 _vel;
        private bool _dragging;

        // tween state
        private Vector3 _baseScale;
        private Tween _scaleTween;

        // invalid "no" shake timer
        private float _noShakeT;
        private float _noShakeDur;
        private float _noShakeAmpDeg;

        private SkeletonAnimation _sa;
        private Bone _headBone;
        private Bone _faceBone;
        private Bone _earLBone;
        private Bone _earRBone;
        private Bone _tailBone;
        private Bone _mouthBone;
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

        private bool _invalidHatchActive;
        private float _hatchScale = 4f;

#if SPINE_UNITY
        private MeshRenderer _skeletonRenderer;
        private MaterialPropertyBlock _hatchBlock;

        private static readonly int HatchStrengthId = Shader.PropertyToID("_HatchStrength");
        private static readonly int HatchColorId = Shader.PropertyToID("_HatchColor");
        private static readonly int HatchScaleId = Shader.PropertyToID("_HatchScale");
        private static readonly int HatchWidthId = Shader.PropertyToID("_HatchWidth");
        private static readonly int HatchAngleId = Shader.PropertyToID("_HatchAngleDeg");
        private static readonly int HatchOpacityId = Shader.PropertyToID("_HatchOpacity");
#endif

        private enum SpineState
        {
            Idle,
            Dragging
        }

        private void Awake()
        {
            if (visualRoot == null) visualRoot = transform;
            _baseScale = visualRoot.localScale;
            _lastPos = visualRoot.position;

            _sa = ResolveSkeletonAnimation();
            BindSkeletonEvents();
            TryBindBones(logSuccess: false);
            ApplySpineState(SpineState.Idle, force: true);
            ApplyHatchOverlay();
        }

        private void OnEnable()
        {
            if (visualRoot == null) visualRoot = transform;
            _baseScale = visualRoot.localScale;
            _lastPos = visualRoot.position;

            _sa = ResolveSkeletonAnimation();
            BindSkeletonEvents();
            TryBindBones(logSuccess: false);
            ApplySpineState(SpineState.Idle, force: true);
            ApplyHatchOverlay();
        }

        private void OnDisable()
        {
            UnbindSkeletonEvents();
        }

        public void SetDragging(bool dragging)
        {
            _dragging = dragging;
            ApplySpineState(dragging ? SpineState.Dragging : SpineState.Idle);
        }

        public void SetInvalidHatch(bool enabled)
        {
            if (settings != null && !settings.enableInvalidHatch) enabled = false;
            if (_invalidHatchActive == enabled) return;
            _invalidHatchActive = enabled;
            ApplyHatchOverlay();
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
            if (!Validate()) return;
            Impact(settings.dropValidImpactScale);
        }

        public void OnDropInvalid()
        {
            if (!Validate()) return;
            Impact(settings.dropInvalidImpactScale);
            TriggerNoShake();
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
            TickDragScale();
            TickNoShake();
            TickIdle();
            TickBoneFollow();
        }

        private void TickIdle()
        {
            if (_spineState != SpineState.Idle) return;
            _idleT += Time.deltaTime;
        }

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
            if (settings == null || !settings.enableBoneFollow) return;
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

        private void TickBoneFollow()
        {
            if (!Validate() || !settings.enableBoneFollow) return;
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
            if (!Validate() || !settings.enableBoneFollow) return;
            if (_headBone == null && _faceBone == null && _earLBone == null && _earRBone == null && _tailBone == null && _mouthBone == null)
                TryBindBones(logSuccess: false);
            if (_headBone != null) _headBone.Rotation = _headRotDeg;
            if (_faceBone != null)
            {
                _faceBone.X = _faceX;
                _faceBone.Y = _faceY;
            }
        }

        private void TryBindBones(bool logSuccess)
        {
            if (_sa == null || _sa.Skeleton == null || settings == null) return;

            if (!string.IsNullOrEmpty(settings.headBoneName))
                _headBone = _sa.Skeleton.FindBone(settings.headBoneName);

            if (!string.IsNullOrEmpty(settings.faceBoneName))
                _faceBone = _sa.Skeleton.FindBone(settings.faceBoneName);

            if (!string.IsNullOrEmpty(settings.earLBoneName))
                _earLBone = _sa.Skeleton.FindBone(settings.earLBoneName);

            if (!string.IsNullOrEmpty(settings.earRBoneName))
                _earRBone = _sa.Skeleton.FindBone(settings.earRBoneName);

            if (!string.IsNullOrEmpty(settings.tailBoneName))
                _tailBone = _sa.Skeleton.FindBone(settings.tailBoneName);

            if (!string.IsNullOrEmpty(settings.mouthBoneName))
                _mouthBone = _sa.Skeleton.FindBone(settings.mouthBoneName);

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
                    float mix = settings != null ? settings.dragStopMixDuration : 0f;
                    _sa.AnimationState.SetEmptyAnimation(0, Mathf.Max(0f, mix));
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

        private void ApplyHatchOverlay()
        {
#if SPINE_UNITY
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null) return;
            if (_skeletonRenderer == null) _skeletonRenderer = _sa.GetComponent<MeshRenderer>();
            if (_skeletonRenderer == null) return;

            if (_hatchBlock == null) _hatchBlock = new MaterialPropertyBlock();
            _skeletonRenderer.GetPropertyBlock(_hatchBlock);
            float hatchStrength = _invalidHatchActive && settings != null ? settings.hatchStrength : 0f;
            _hatchBlock.SetFloat(HatchStrengthId, hatchStrength);
            _hatchBlock.SetColor(HatchColorId, settings != null ? settings.hatchColor : Color.black);
            _hatchBlock.SetFloat(HatchScaleId, _hatchScale);
            _hatchBlock.SetFloat(HatchWidthId, settings != null ? settings.hatchWidth : 0.18f);
            _hatchBlock.SetFloat(HatchAngleId, settings != null ? settings.hatchAngleDeg : 45f);
            _hatchBlock.SetFloat(HatchOpacityId, settings != null ? settings.hatchOpacity : 0.8f);
            _skeletonRenderer.SetPropertyBlock(_hatchBlock);
#endif
        }

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
    }
}