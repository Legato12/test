
using System.Runtime.InteropServices;
using UnityEngine;

namespace Phase0
{
    /// <summary>
    /// Cross-platform native haptics for Phase0.
    /// iOS: Taptic Engine (UIFeedbackGenerator) — Light/Medium/Heavy/Selection/Notification.
    /// Android: VibrationEffect with amplitude control (API 26+), fallback to legacy vibrate.
    /// Editor/other: silent no-op (Debug.Log only in dev builds).
    ///
    /// Zero-allocation on hot path. No coroutines for single pulses.
    /// Coroutine only used for multi-pulse patterns on Android (iOS handles patterns natively).
    /// </summary>
    public static class Phase0Haptics
    {
        // ──────────────────────────────────────────────
        // iOS native imports
        // ──────────────────────────────────────────────
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _NativeHaptics_ImpactLight();
        [DllImport("__Internal")] private static extern void _NativeHaptics_ImpactMedium();
        [DllImport("__Internal")] private static extern void _NativeHaptics_ImpactHeavy();
        [DllImport("__Internal")] private static extern void _NativeHaptics_ImpactWithIntensity(int style, float intensity);
        [DllImport("__Internal")] private static extern void _NativeHaptics_NotificationSuccess();
        [DllImport("__Internal")] private static extern void _NativeHaptics_NotificationWarning();
        [DllImport("__Internal")] private static extern void _NativeHaptics_NotificationError();
        [DllImport("__Internal")] private static extern void _NativeHaptics_SelectionTick();
#endif

        // ──────────────────────────────────────────────
        // Android native bridge (cached AndroidJavaClass)
        // ──────────────────────────────────────────────
#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaClass _androidHaptics;
        private static bool _androidChecked;

        private static AndroidJavaClass AndroidBridge
        {
            get
            {
                if (!_androidChecked)
                {
                    _androidChecked = true;
                    try
                    {
                        _androidHaptics = new AndroidJavaClass("com.phase0.haptics.NativeHaptics");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Phase0Haptics: Android native bridge failed: {e.Message}");
                    }
                }
                return _androidHaptics;
            }
        }
#endif

        // ══════════════════════════════════════════════
        //  PUBLIC API — вызывай эти методы из GameController
        // ══════════════════════════════════════════════

        /// <summary>
        /// ON GRAB — Едва заметный тик при начале перетаскивания.
        /// iOS: Selection tick (самый лёгкий, ~6мс Taptic).
        /// Android: 12мс вибрация, амплитуда 60/255.
        /// </summary>
        public static void OnGrab()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _NativeHaptics_SelectionTick();
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge?.CallStatic("vibrate", 12L, 60);
#else
            LogHaptic("OnGrab (selection tick, 12ms)");
#endif
        }

        /// <summary>
        /// ON VALID PLACE — Чёткий "щелчок" при успешном размещении.
        /// iOS: Impact Medium (ощутимый Taptic щелчок).
        /// Android: 28мс вибрация, амплитуда 180/255.
        /// </summary>
        public static void OnValidPlace()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _NativeHaptics_ImpactMedium();
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge?.CallStatic("vibrate", 28L, 180);
#else
            LogHaptic("OnValidPlace (impact medium, 28ms)");
#endif
        }

        /// <summary>
        /// ON INVALID/REJECT — Двойной "бзз-бзз" отказа.
        /// iOS: Notification Error (системный паттерн "ошибка" — двойной Taptic).
        /// Android: паттерн [0, 15, 50, 15] с амплитудами [0, 120, 0, 120].
        /// </summary>
        public static void OnReject()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _NativeHaptics_NotificationError();
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge?.CallStatic("vibratePattern",
                new long[] { 0L, 15L, 50L, 15L },
                new int[] { 0, 120, 0, 120 }
            );
#else
            LogHaptic("OnReject (double buzz: 15ms-50ms-15ms)");
#endif
        }

        /// <summary>
        /// ON ROTATE TAP — Микро-тик при повороте фигуры тапом.
        /// iOS: Impact Light с интенсивностью 0.4 (нежный).
        /// Android: 8мс вибрация, амплитуда 40/255.
        /// </summary>
        public static void OnRotate()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _NativeHaptics_ImpactWithIntensity(0, 0.4f); // 0 = UIImpactFeedbackStyleLight
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge?.CallStatic("vibrate", 8L, 40);
#else
            LogHaptic("OnRotate (light impact, 8ms)");
#endif
        }

        /// <summary>
        /// ON HOVER SNAP — Тактильная "сетка" при перетаскивании по клеткам.
        /// Вызывать при смене hoveredCell. Самый тихий.
        /// iOS: Selection tick.
        /// Android: 6мс вибрация, амплитуда 30/255.
        /// </summary>
        public static void OnHoverSnap()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _NativeHaptics_SelectionTick();
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge?.CallStatic("vibrate", 6L, 30);
#else
            LogHaptic("OnHoverSnap (selection tick, 6ms)");
#endif
        }

        // ──────────────────────────────────────────────
        // Legacy API (для обратной совместимости, если где-то вызывается)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Legacy: маршрутизирует по count. Лучше вызывать конкретные методы напрямую.
        /// count=1 → OnGrab, count=2 → OnValidPlace, count>=3 → OnReject
        /// </summary>
        public static void Pulse(MonoBehaviour owner, int count = 1, float intervalSeconds = 0.05f)
        {
            if (count <= 1)
                OnGrab();
            else if (count == 2)
                OnValidPlace();
            else
                OnReject();
        }

        public static void Simple()
        {
            OnGrab();
        }

        // ──────────────────────────────────────────────
        // Debug
        // ──────────────────────────────────────────────
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogHaptic(string label)
        {
            Debug.Log($"[Haptic] {label}");
        }
    }
}