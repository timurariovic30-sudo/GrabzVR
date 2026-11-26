using UnityEngine;
using System;

namespace Photon.Voice.Unity
{
    public class MicWrapperPusher : IAudioPusher<float>
    {
        private AudioSource audioSource;
        private AudioClip mic;
        private string device;
        private ILogger logger;
        private MicWrapperPusherOnAudioFilterRead onRead;

        private int sampleRate;
        private int channels;

        public MicWrapperPusher(GameObject parent, string device, int suggestedFrequency, ILogger logger)
        {
            try
            {
                this.device = device;
                this.logger = logger;

                this.sampleRate = AudioSettings.outputSampleRate;
                switch (AudioSettings.speakerMode)
                {
                    case AudioSpeakerMode.Mono: this.channels = 1; break;
                    case AudioSpeakerMode.Stereo: this.channels = 2; break;
                    default:
                        Error = "Only Mono and Stereo project speaker mode supported. Current mode is " + AudioSettings.speakerMode;
                        logger.Log(LogLevel.Error, "[PV] MicWrapperPusher: " + Error);
                        return;
                }

                int frequency;
                this.Error = UnityMicrophone.CheckDevice(logger, "[PV] MicWrapperPusher: ", device, suggestedFrequency, out frequency);
                if (this.Error != null)
                {
                    return;
                }

                GameObject go = new GameObject("[PV] MicWrapperPusher: AudioSource + AudioOutCapture");
                go.transform.SetParent(parent.transform, false);

                this.audioSource = go.AddComponent<AudioSource>();
                logger.Log(LogLevel.Info, "[PV] MicWrapperPusher: new AudioSource created.");

                this.onRead = go.AddComponent<MicWrapperPusherOnAudioFilterRead>();

                this.mic = UnityMicrophone.Start(device, true, 1, frequency);
                if (this.mic == null)
                {
                    Error = "Microphone.Start() returned null";
                    logger.Log(LogLevel.Error, "[PV] MicWrapperPusher: " + Error);
                    return;
                }
                audioSource.clip = mic;
                audioSource.loop = true;

                // Without waiting for the mic to start, samples are read with a significant dealy and distortion.
                // The original code (https://stackoverflow.com/questions/53376891/how-to-read-the-data-from-audioclip-using-pcmreadercallback-when-the-former-is-c):
                // while (!(Microphone.GetPosition(device) > 0)) { }
                for (var i = 0; i < 1000; i++)
                {
                    if (UnityMicrophone.GetPosition(device) > 0)
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(1);
                }
                if (UnityMicrophone.GetPosition(device) <= 0)
                {
                    logger.Log(LogLevel.Warning, "[PV] MicWrapperPusher: microphone start takes too long, Playing audio source without waiting for the microphone. Captured data may be delayed.");
                }

                this.audioSource.Play();

                logger.Log(LogLevel.Info, "[PV] MicWrapperPusher: microphone '{0}' initialized, frequency = {1}, channels = {2}.", device, this.mic.frequency, this.mic.channels);
            }
            catch (Exception e)
            {
                Error = e.ToString();
                if (Error == null) // should never happen but since Error used as validity flag, make sure that it's not null
                {
                    Error = "Exception in MicWrapperPusher constructor";
                }
                logger.Log(LogLevel.Error, "[PV] MicWrapperPusher: " + Error);
            }
        }

        public void SetCallback(Action<float[]> callback, ObjectFactory<float[], int> bufferFactory, int optimalFrameSize)
        {
            onRead.OnAudioFrame += (buf, ch) =>
            {
                // the pushed buffer is queued and later freed into bufferFactory
                var b = bufferFactory.New(buf.Length);
                Array.Copy(buf, b, buf.Length);
                callback(b);
            };
        }

        public void Dispose()
        {
            if (this.mic != null) // started by this pusher (End() throws on WebGL)
            {
                UnityMicrophone.End(this.device);
            }
            if (audioSource != null)
            {
                GameObject.Destroy(audioSource.gameObject); // remove dynamically created object
                logger.Log(LogLevel.Info, "[PV] MicWrapperPusher: AudioSource removed.");
            }
        }

        public int SamplingRate { get { return Error == null ? this.sampleRate : 0; } }
        public int Channels { get { return Error == null ? this.channels : 0; } }
        public string Error { get; private set; }

    }

    class MicWrapperPusherOnAudioFilterRead : MonoBehaviour
    {
        public event Action<float[], int> OnAudioFrame;
        void OnAudioFilterRead(float[] frame, int channels)
        {
            var onAudioFrame = OnAudioFrame;
            if (onAudioFrame != null)
            {
                onAudioFrame(frame, channels); // the subscriber copies the frame
            }
            Array.Clear(frame, 0, frame.Length);
        }
    }
}