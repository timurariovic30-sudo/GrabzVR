#if ((UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR) || __IOS__

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Photon.Voice.IOS
{
    public class AudioInChangeNotifier : IAudioInChangeNotifier
    {
        public bool IsSupported => true;

        const string lib_name = "__Internal";
        [DllImport(lib_name)]
        private static extern IntPtr Photon_Audio_In_CreateChangeNotifier(int instanceID, Action<int> callback);
        [DllImport(lib_name)]
        private static extern IntPtr Photon_Audio_In_DestroyChangeNotifier(IntPtr handle);

        private delegate void CallbackDelegate(int instanceID);

        IntPtr handle;
        int instanceID;
        Action callback;
        ILogger logger;

        public AudioInChangeNotifier(Action callback, ILogger logger)
        {
            this.callback = callback;
            this.logger = logger;
            var handle = Photon_Audio_In_CreateChangeNotifier(instanceCnt, nativeCallback);
            lock (instancePerHandle)
            {
                this.handle = handle;
                this.instanceID = instanceCnt;
                instancePerHandle.Add(instanceCnt++, this);
            }
        }

        // IL2CPP does not support marshaling delegates that point to instance methods to native code.
        // Using static method and per instance table.
        static int instanceCnt;
        private static Dictionary<int, AudioInChangeNotifier> instancePerHandle = new Dictionary<int, AudioInChangeNotifier>();
        [MonoPInvokeCallbackAttribute(typeof(CallbackDelegate))]
        private static void nativeCallback(int instanceID)
        {
            AudioInChangeNotifier instance;
            bool ok;
            lock (instancePerHandle)
            {
                ok = instancePerHandle.TryGetValue(instanceID, out instance);
            }
            if (ok)
            {
                try
                {
                    instance.callback();
                }
                catch (Exception e)
                {
                    instance.logger.Log(LogLevel.Error, "[PV] AudioInChangeNotifier: exception in callback: " + e);
                }
            }
        }

        /// <summary>If not null, the enumerator is in invalid state.</summary>
        public string Error { get; private set; }

        /// <summary>Disposes enumerator.
        /// Call it to free native resources.
        /// </summary>
        public void Dispose()
        {
            lock (instancePerHandle)
            {
                instancePerHandle.Remove(instanceID);
            }
            if (handle != IntPtr.Zero)
            {
                Photon_Audio_In_DestroyChangeNotifier(handle);
                handle = IntPtr.Zero;
            }
        }
    }
}
#endif
