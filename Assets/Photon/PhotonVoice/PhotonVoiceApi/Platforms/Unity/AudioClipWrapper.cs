using UnityEngine;

namespace Photon.Voice.Unity
{
    // Wraps UnityEngine.AudioClip with Voice.IAudioStream interface.
    // Used for playing back audio clips via Photon Voice.
    public class AudioClipWrapper : IAudioReader<float>
    {
        private AudioClip audioClip;
        private int readPos;
        private float startTime;
        public bool Loop { get; set; }

        public AudioClipWrapper(AudioClip audioClip)
        {
            this.audioClip = audioClip;
            startTime = Time.unscaledTime;
        }
        private bool playing = true;
        public bool Read(float[] buffer)
        {
            if (!playing)
            {
                return false;
            }

            var playerPos = (int)((Time.unscaledTime - startTime) * audioClip.frequency);
            var bufferSamplesCount = buffer.Length / audioClip.channels;
            if (playerPos > readPos + bufferSamplesCount)
            {
                if (!this.audioClip.GetData(buffer, readPos)) // e.g. clip data not loaded
                {
                    return false;
                }
                readPos += bufferSamplesCount;

                // GetData() wraps around and reads the clip start into the buffer tail
                if (readPos >= audioClip.samples)
                {
                    if (this.Loop)
                    {
                        readPos -= audioClip.samples;
                        startTime += (float)audioClip.samples / audioClip.frequency;
                    }
                    else
                    {
                        var tail = (readPos - audioClip.samples) * audioClip.channels;
                        System.Array.Clear(buffer, buffer.Length - tail, tail);
                        playing = false;
                    }
                }

                return true;
            }
            else
            {
                return false;
            }
        }
        public int SamplingRate { get { return this.audioClip.frequency; } }
        public int Channels { get { return this.audioClip.channels; } }
        public string Error { get; private set; }

        public void Dispose()
        {

        }
    }
}