using UnityEngine;

namespace TiltBrush
{
    /// <summary>
    /// Lightweight Proxy Bridge Component.
    /// This script lives dynamically on whichever GameObject holds the active AudioListener.
    /// It intercepts the native Unity DSP graph ticks and forwards raw floats to the master manager.
    /// </summary>
    public class AudioFilterProxy : MonoBehaviour
    {
        // AudioFilterProxy.OnAudioFilterRead
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (AudioCaptureManager.m_Instance != null)
            {
                AudioCaptureManager.m_Instance.AppendHiFiSamples(data, channels);
            }
        }
    }
}