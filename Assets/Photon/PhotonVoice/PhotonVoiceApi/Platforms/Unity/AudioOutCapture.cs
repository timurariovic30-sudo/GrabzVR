using UnityEngine;
using System;

namespace Photon.Voice.Unity
{
    public class AudioOutCapture : MonoBehaviour
    {
        public event Action<float[], int> OnAudioFrame;

        void OnAudioFilterRead(float[] frame, int channels)
        {
            OnAudioFrame?.Invoke(frame, channels); // single read: the event may be unsubscribed on the main thread
        }
    }
}