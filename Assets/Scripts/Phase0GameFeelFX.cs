using System;
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

        private Vector3 _lastPos;
        private Vector3 _vel;
        private bool _dragging;

        // tween state
        private Vector3 _baseScale;
        private Vector3 _fromScale, _toScale;
        private float _t, _dur;
        private Func<float, float> _ease;
        private bool _tweening;
        private Vector3 _overshootTarget;
        private bool _pendingOvershoot;

        // invalid "no" shake timer
        private float _noShakeT;
        private float _noShakeDur;
        private float _noShakeAmpDeg;

#if SPINE_UNITY
        private SkeletonAnimation _sa;
        private Bone _headBone;
        private Bone _faceBone;
        private float _headRotDeg;
        private float _faceX;
        private float _faceY;
        private float _savedSpineTimeScale = 1f;
        private bool _spinePaused;
#endif

        private void Awake()
        {
            if (visualRoot == null) visualRoot = transform;
            _baseScale = visualRoot.localScale;
            _lastPos = visualRoot.position;

#if SPINE_UNITY
            _sa = ResolveSkeletonAnimation();
            BindSkeletonEvents();
            TryBindBones();
#endif
        }

        private void OnEnable()
        {
            if (visualRoot == null) visualRoot = transform;
            _baseScale = visualRoot.localScale;
            _lastPos = visualRoot.position;

#if SPINE_UNITY
            _sa = ResolveSkeletonAnimation();
            BindSkeletonEvents();
#endif
        }

        private void OnDisable()
        {
#if SPINE_UNITY
            UnbindSkeletonEvents();
#endif
        }

        public void SetDragging(bool dragging)
        {
            _dragging = dragging;
#if SPINE_UNITY
            if (settings != null && settings.pauseSpineWhileDragging)
            {
                _sa = ResolveSkeletonAnimation();
                if (_sa != null && _sa.AnimationState != null)
                {
                    if (dragging && !_spinePaused)
                    {
                        _savedSpineTimeScale = _sa.timeScale;
                        _sa.timeScale = 0f;
                        _spinePaused = true;
                    }
                    else if (!dragging && _spinePaused)
                    {
                        _sa.timeScale = _savedSpineTimeScale;
                        _spinePaused = false;
                    }
                }
            }
#endif
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
            TweenScale(new Vector3(settings.pickupScale.x, settings.pickupScale.y, 1f),
                settings.quickScaleDuration, EaseOutBack);
            QueueReturnToBase();
        }

        public void OnRotateTap()
        {
            if (!Validate()) return;
            TweenScale(new Vector3(settings.rotateTapScale.x, settings.rotateTapScale.y, 1f),
                settings.quickScaleDuration, EaseOutBack);
            QueueReturnToBase();
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
            TweenScale(new Vector3(impact.x, impact.y, 1f),
                settings.quickScaleDuration, EaseOutExpo);

            _overshootTarget = new Vector3(settings.overshootScale.x, settings.overshootScale.y, 1f);
            _pendingOvershoot = true;
        }

        private void QueueReturnToBase()
        {
            _overshootTarget = _baseScale;
            _pendingOvershoot = true;
        }

        private bool Validate() => visualRoot != null && settings != null;

        private void Update()
        {
            TickScaleTween();
            TickDragScale();
            TickNoShake();
            TickBoneFollow();
        }

        private void TickScaleTween()
        {
            if (!_tweening) return;

            _t += Time.deltaTime;
            float u = _dur <= 1e-5f ? 1f : Mathf.Clamp01(_t / _dur);
            float e = _ease != null ? _ease(u) : u;
            visualRoot.localScale = Vector3.LerpUnclamped(_fromScale, _toScale, e);

            if (u >= 1f)
            {
                _tweening = false;
                if (_pendingOvershoot)
                {
                    _pendingOvershoot = false;
                    TweenScale(_overshootTarget, settings.returnDuration, EaseOutBack);
                }
            }
        }

        private void TickDragScale()
        {
            if (!Validate() || !_dragging) return;
            if (settings.dragSpeedScaleAmount <= 0f) return;

            float speed = _vel.magnitude;
            float k = Mathf.Clamp01(speed / Mathf.Max(settings.dragSpeedForMax, 0.01f));
            float amt = settings.dragSpeedScaleAmount * k;

            Vector3 target = new Vector3(_baseScale.x * (1f + amt), _baseScale.y * (1f - amt), _baseScale.z);

            if (!_tweening)
                visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, target, 0.25f);
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
#if SPINE_UNITY
            if (!Validate() || !settings.enableBoneFollow) return;
            if (_sa == null) _sa = ResolveSkeletonAnimation();
            if (_sa == null || _sa.Skeleton == null)
            {
                return;
            }

            if (_headBone == null && _faceBone == null) TryBindBones();

            // compute targets; actual bone write happens in Spine UpdateLocal hook
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            float velScale = Mathf.Max(settings.boneVelocityScale, 0.01f);
            float vx = _vel.x * velScale;
            float vy = _vel.y * velScale;
            float kx = Mathf.Clamp(vx, -1f, 1f);
            float ky = Mathf.Clamp(vy, -1f, 1f);

            float desiredHead = Mathf.Clamp((-kx) * settings.headTiltDegrees,
                -settings.headTiltDegrees, settings.headTiltDegrees);

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

            float b = 1f - Mathf.Exp(-settings.faceFollowStiffness * dt);
            _faceX = Mathf.Lerp(_faceX, desiredFaceX, b);
            _faceY = Mathf.Lerp(_faceY, desiredFaceY, b);
#endif
        }

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
            if (!Validate() || !settings.enableBoneFollow) return;
            if (_headBone == null && _faceBone == null) TryBindBones();
            if (_headBone != null) _headBone.Rotation = _headRotDeg;
            if (_faceBone != null)
            {
                _faceBone.X = _faceX;
                _faceBone.Y = _faceY;
            }
        }

        private void TryBindBones()
        {
            if (_sa == null || _sa.Skeleton == null || settings == null) return;

            if (!string.IsNullOrEmpty(settings.headBoneName))
                _headBone = _sa.Skeleton.FindBone(settings.headBoneName);

            if (!string.IsNullOrEmpty(settings.faceBoneName))
                _faceBone = _sa.Skeleton.FindBone(settings.faceBoneName);
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
#endif

        private void TweenScale(Vector3 to, float duration, Func<float, float> ease)
        {
            _fromScale = visualRoot.localScale;
            _toScale = to;
            _dur = Mathf.Max(0f, duration);
            _t = 0f;
            _ease = ease;
            _tweening = true;
        }

        private static float EaseOutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}