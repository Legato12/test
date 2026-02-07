using System.Collections;
using UnityEngine;

namespace Phase0
{
    /// <summary>
    /// Cross-platform haptics helper (Android/iOS). Safe no-op in Editor/other platforms.
    /// </summary>
    public static class Phase0Haptics
    {
        public static void Pulse(MonoBehaviour owner, int count = 1, float intervalSeconds = 0.05f)
        {
            if (owner == null || count <= 1)
            {
                Simple();
                return;
            }

            owner.StartCoroutine(PulseRoutine(count, intervalSeconds));
        }

        public static void Simple()
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        private static IEnumerator PulseRoutine(int count, float intervalSeconds)
        {
            int c = Mathf.Max(1, count);
            float wait = Mathf.Max(0.01f, intervalSeconds);
            var waitYield = new WaitForSeconds(wait);

            for (int i = 0; i < c; i++)
            {
                Simple();
                if (i < c - 1)
                    yield return waitYield;
            }
        }
    }
}