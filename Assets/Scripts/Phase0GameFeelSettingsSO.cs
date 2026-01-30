using UnityEngine;

namespace Phase0
{
    [CreateAssetMenu(menuName = "Phase0/Game Feel Settings", fileName = "Phase0GameFeelSettings")]
    public class Phase0GameFeelSettingsSO : ScriptableObject
    {
        [Header("General")]
        [Min(0f)] public float quickScaleDuration = 0.08f;
        [Min(0f)] public float returnDuration = 0.12f;

        [Header("Pickup Squash")]
        public Vector2 pickupScale = new Vector2(1.06f, 0.94f);

        [Header("Drop Squash")]
        public Vector2 dropValidImpactScale = new Vector2(0.92f, 1.08f);
        public Vector2 dropInvalidImpactScale = new Vector2(0.88f, 1.14f);
        public Vector2 overshootScale = new Vector2(1.03f, 0.97f);

        [Header("Drag Feel (optional)")]
        [Range(0f, 0.2f)] public float dragSpeedScaleAmount = 0.04f;
        [Min(0.01f)] public float dragSpeedForMax = 6f;

        [Header("Rotate Tap")]
        public Vector2 rotateTapScale = new Vector2(1.03f, 0.97f);

        [Header("Bone Follow (code-driven bones)")]
        public bool enableBoneFollow = true;
        public string headBoneName = "head";
        public string faceBoneName = "face";
        [Min(0.01f)] public float boneVelocityScale = 4f;
        [Range(0f, 25f)] public float headTiltDegrees = 10f;
        [Range(1f, 40f)] public float headFollowStiffness = 18f;
        [Range(0f, 30f)] public float headNoShakeDegrees = 14f;
        [Min(0.01f)] public float headNoShakeDuration = 0.28f;
        [Range(0f, 20f)] public float faceBob = 6f;
        [Range(1f, 40f)] public float faceFollowStiffness = 14f;
        [Header("Face Clamp (local bone offsets)")]
        public Vector2 faceOffsetXMinMax = new Vector2(-20f, 70f);
        public Vector2 faceOffsetYMinMax = new Vector2(-40f, 40f);
        [Header("Spine Animation")]
        public bool pauseSpineWhileDragging = true;
        [Min(0f)] public float dragStopMixDuration = 0.08f;
        [Tooltip("If true, plays idleAnimationName on state Idle. Leave name empty to keep current.")]
        public bool playIdleAnimation = true;
        public string idleAnimationName = "animation";

        [Header("Spine Idle (procedural)")]
        [Tooltip("Adds a subtle procedural idle even if no animation is playing.")]
        public bool enableIdleBreathing = true;
        [Range(0f, 20f)] public float idleHeadBreathDegrees = 3f;
        [Range(0f, 20f)] public float idleFaceBreath = 2f;
        [Min(0.01f)] public float idleBreathSpeed = 1.5f;

        [Header("Spine Debug")]
        public bool logMissingBones = true;
        public bool logBoneBindSuccess = false;

        [Header("Optional Extra Bones")]
        public string earLBoneName = "ear_L";
        public string earRBoneName = "ear_R";
        public string tailBoneName = "Tail";
        public string mouthBoneName = "mouth";
    }
}