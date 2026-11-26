#if PHOTON_VOICE_WINDOWS || UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Photon.Voice.Windows
{
    public class WindowsAudioInPusher : IAudioPusher<short>
    {
        enum SystemMode
        {
            SINGLE_CHANNEL_AEC = 0,
            OPTIBEAM_ARRAY_ONLY = 2,
            OPTIBEAM_ARRAY_AND_AEC = 4,
            SINGLE_CHANNEL_NSAGC = 5,
        }

        [DllImport("AudioIn", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Photon_Audio_In_Create(int instanceID, SystemMode systemMode, int micDevIdx, int spkDevIdx, CallbackDelegate callback, bool featrModeOn, bool noiseSup, bool agc, bool cntrClip);

        [DllImport("AudioIn", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Photon_Audio_In_Destroy(IntPtr handler);

        // native callback is __stdcall (the delegate marshaling default)
        private delegate void CallbackDelegate(int instanceID, IntPtr buf, int len);

        IntPtr handle;
        int instanceID;
        Action<short[]> pushCallback;
        ObjectFactory<short[], int> bufferFactory;
        ILogger logger;

        public WindowsAudioInPusher(int deviceID, ILogger logger)
        {
            this.logger = logger;
            lock (instancePerHandle)
            {
                this.instanceID = instanceCnt;
                instancePerHandle.Add(instanceCnt++, this);
            }
            try
            {
                // not under the lock: the native capture thread may call nativePushCallback (locking instancePerHandle) before Create returns
                // use default playback device
                handle = Photon_Audio_In_Create(instanceID, SystemMode.SINGLE_CHANNEL_AEC, deviceID, -1, nativePushCallbackRef, true, true, true, true); // defaults in original ms sample: false, true, false, false
                if (handle == IntPtr.Zero)
                {
                    Error = "Photon_Audio_In_Create() failed";
                }
            }
            catch (Exception e)
            {
                Error = e.ToString();
                if (Error == null) // should never happen but since Error used as validity flag, make sure that it's not null
                {
                    Error = "Exception in WindowsAudioInPusher constructor";
                }
            }
            if (Error != null)
            {
                lock (instancePerHandle)
                {
                    instancePerHandle.Remove(instanceID);
                }
                logger.Log(LogLevel.Error, "[PV] WindowsAudioInPusher: " + Error);
            }
        }

        // IL2CPP does not support marshaling delegates that point to instance methods to native code.
        // Using static method and per instance table.
        static int instanceCnt;
        private static Dictionary<int, WindowsAudioInPusher> instancePerHandle = new Dictionary<int, WindowsAudioInPusher>();
        // the native side stores the callback: keep the marshaled delegate alive
        private static CallbackDelegate nativePushCallbackRef = new CallbackDelegate(nativePushCallback);
        [MonoPInvokeCallbackAttribute(typeof(CallbackDelegate))]
        private static void nativePushCallback(int instanceID, IntPtr buf, int len)
        {
            WindowsAudioInPusher instance;
            bool ok;
            lock (instancePerHandle)
            {
                ok = instancePerHandle.TryGetValue(instanceID, out instance);
            }
            if (ok)
            {
                try
                {
                    instance.push(buf, len);
                }
                catch (Exception e)
                {
                    instance.logger.Log(LogLevel.Error, "[PV] WindowsAudioInPusher: " + e);
                }
            }
        }

        // Supposed to be called once at voice initialization.
        // Otherwise recreate native object (instead of adding 'set callback' method to native interface)
        public void SetCallback(Action<short[]> callback, ObjectFactory<short[], int> bufferFactory, int optimalFrameSize)
        {
            this.bufferFactory = bufferFactory;
            this.pushCallback = callback;
        }

        private void push(IntPtr buf, int lenBytes)
        {
            if (pushCallback != null)
            {
                var len = lenBytes / sizeof(short);
                var bufManaged = this.bufferFactory.New(len);
                Marshal.Copy(buf, bufManaged, 0, len);
                pushCallback(bufManaged);
            }
        }

        public int Channels { get { return 1; } }

        // Hardcoded in AudioInAec.cpp
        // Supported sample rates: 8000, 11025, 16000, 22050
        // https://docs.microsoft.com/en-us/windows/win32/medfound/voicecapturedmo?redirectedfrom=MSDN (Voice Capture DSP)
        public int SamplingRate { get { return 16000; } }

        public string Error { get; private set; }

        public void Dispose()
        {
            lock (instancePerHandle)
            {
                instancePerHandle.Remove(instanceID);
            }
            if (handle != IntPtr.Zero)
            {
                Photon_Audio_In_Destroy(handle);
                handle = IntPtr.Zero;
            }
            // TODO: Remove this from instancePerHandle
        }
    }
}
#endif