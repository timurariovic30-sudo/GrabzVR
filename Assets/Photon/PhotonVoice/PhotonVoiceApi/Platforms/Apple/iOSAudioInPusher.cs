#if ((UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR) || __IOS__
using System;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Photon.Voice.IOS
{
    public class AudioInPusher : IAudioPusher<float>, IResettable
    {
        const string lib_name = "__Internal";
        [DllImport(lib_name)]
        private static extern IntPtr Photon_Audio_In_CreatePusher(int instanceID, Action<int, IntPtr, int> pushCallback, int sessionCategory, int sessionMode, int sessionCategoryOptions);
        [DllImport(lib_name)]
        private static extern void Photon_Audio_In_Reset(IntPtr handler);
        [DllImport(lib_name)]
        private static extern void Photon_Audio_In_Destroy(IntPtr handler);

        private delegate void CallbackDelegate(int instanceID, IntPtr buf, int len);

        public AudioInPusher(AudioSessionParameters sessParam, ILogger logger)
        {
            this.logger = logger;
            // initialization in a separate thread to avoid 0.5 - 1 sec. pauses in main thread execution
            var t = new Thread(() =>
            {
                lock (nativeLock) // prevent concurrent initialization
                {
                    if (disposed) // Dispose() thread ran first
                    {
                        return;
                    }
                    try
                    {
                        var handle = Photon_Audio_In_CreatePusher(instanceCnt, nativePushCallback, (int)sessParam.Category, (int)sessParam.Mode, sessParam.CategoryOptionsToInt());
                        this.handle = handle;
                        if (handle == IntPtr.Zero)
                        {
                            Error = "Native AudioInPusher creation error";
                            logger.Log(LogLevel.Error, "[PV] AudioInPusher: " + Error);
                            return;
                        }
                        lock (instancePerHandle)
                        {
                            this.instanceID = instanceCnt;
                            instancePerHandle.Add(instanceCnt++, this);
                        }
                    }
                    catch (Exception e)
                    {
                        Error = e.ToString();
                        if (Error == null) // should never happen but since Error used as validity flag, make sure that it's not null
                        {
                            Error = "Exception in AudioInPusher constructor";
                        }
                        logger.Log(LogLevel.Error, "[PV] AudioInPusher: " + Error);
                    }
                }
            });
            Util.SetThreadName(t, "[PV] IOSAudioInPusherCtr");
            t.Start();
        }

        // IL2CPP does not support marshaling delegates that point to instance methods to native code.
        // Using static method and per instance table.
        static int instanceCnt;
        private static Dictionary<int, AudioInPusher> instancePerHandle = new Dictionary<int, AudioInPusher>();
        // Serializes native create/reset/destroy. Not taken by the push callback which runs on the main thread.
        private static object nativeLock = new object();
        [MonoPInvokeCallbackAttribute(typeof(CallbackDelegate))]
        private static void nativePushCallback(int instanceID, IntPtr buf, int len)
        {
            AudioInPusher instance;
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
                    instance.logger.Log(LogLevel.Error, "[PV] AudioInPusher: exception in push callback: " + e);
                }
            }
        }

        ILogger logger;
        IntPtr handle;
        int instanceID = -1;
        bool disposed;
        Action<float[]> pushCallback;
        ObjectFactory<float[], int> bufferFactory;

        // Supposed to be called once at voice initialization.
        // Otherwise recreate native object (instead of adding 'set callback' method to native interface)
        public void SetCallback(Action<float[]> callback, ObjectFactory<float[], int> bufferFactory, int optimalFrameSize)
        {
            this.bufferFactory = bufferFactory;
            this.pushCallback = callback;
        }
        private void push(IntPtr buf, int len)
        {
            var pushCallback = this.pushCallback;
            var bufferFactory = this.bufferFactory;
            if (pushCallback != null && bufferFactory != null)
            {
                var bufManaged = bufferFactory.New(len);
                Marshal.Copy(buf, bufManaged, 0, len);
                pushCallback(bufManaged);
            }
        }

        public int Channels { get { return 1; } }

        public int SamplingRate { get { return 48000; } }

        public string Error { get; private set; }

        public void Reset()
        {
            lock (nativeLock)
            {
                if (handle != IntPtr.Zero)
                {
                    Photon_Audio_In_Reset(handle);
                }
            }
        }

        public void Dispose()
        {
            // disopse in a separate thread to avoid pauses in main thread execution
            var t = new Thread(() =>
            {
                lock (nativeLock)
                {
                    disposed = true;
                    lock (instancePerHandle)
                    {
                        instancePerHandle.Remove(instanceID);
                    }

                    if (handle != IntPtr.Zero)
                    {
                        Photon_Audio_In_Destroy(handle);
                        handle = IntPtr.Zero;
                    }
                }
            });
            Util.SetThreadName(t, "[PV] IOSAudioInPusherDisp");
            t.Start();
        }
    }
}
#endif