package com.phase0.haptics;

import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;
import com.unity3d.player.UnityPlayer;

public class NativeHaptics {

    private static Vibrator getVibrator() {
        Context ctx = UnityPlayer.currentActivity;
        if (ctx == null) return null;

        if (Build.VERSION.SDK_INT >= 31) {
            VibratorManager vm = (VibratorManager) ctx.getSystemService(Context.VIBRATOR_MANAGER_SERVICE);
            return vm != null ? vm.getDefaultVibrator() : null;
        }
        return (Vibrator) ctx.getSystemService(Context.VIBRATOR_SERVICE);
    }

    // Single vibration: duration (ms) and amplitude (1-255, or -1 = default)
    public static void vibrate(long milliseconds, int amplitude) {
        try {
            Vibrator v = getVibrator();
            if (v == null || !v.hasVibrator()) return;

            if (Build.VERSION.SDK_INT >= 26) {
                int amp = (amplitude <= 0) ? VibrationEffect.DEFAULT_AMPLITUDE : Math.min(255, amplitude);
                v.vibrate(VibrationEffect.createOneShot(milliseconds, amp));
            } else {
                v.vibrate(milliseconds);
            }
        } catch (Exception e) {
            // SecurityException if VIBRATE permission missing, or other edge cases
        }
    }

    // Pattern: array [pause, vibrate, pause, vibrate, ...] in ms
    // amplitudes: array of amplitudes per segment (0 = pause, 1-255 = vibrate)
    public static void vibratePattern(long[] pattern, int[] amplitudes) {
        try {
            Vibrator v = getVibrator();
            if (v == null || !v.hasVibrator()) return;

            if (Build.VERSION.SDK_INT >= 26 && amplitudes != null && amplitudes.length == pattern.length) {
                v.vibrate(VibrationEffect.createWaveform(pattern, amplitudes, -1));
            } else {
                v.vibrate(pattern, -1);
            }
        } catch (Exception e) {
            // SecurityException if VIBRATE permission missing, or other edge cases
        }
    }

    public static void cancel() {
        try {
            Vibrator v = getVibrator();
            if (v != null) v.cancel();
        } catch (Exception e) {
            // fail silently
        }
    }

    public static boolean hasAmplitudeControl() {
        try {
            if (Build.VERSION.SDK_INT < 26) return false;
            Vibrator v = getVibrator();
            return v != null && v.hasAmplitudeControl();
        } catch (Exception e) {
            return false;
        }
    }
}
