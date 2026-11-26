mergeInto(LibraryManager.library, {
    PhotonVoice_WebRTC_EnumerateDevices: function(requestId, kind, resultCallback) {
        const kindStr = UTF8ToString(kind);
        if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia && navigator.mediaDevices.enumerateDevices) {
            navigator.mediaDevices.getUserMedia({ audio: kindStr.startsWith("audio"), video: kindStr.startsWith("video") }).then(function(stream) {
                navigator.mediaDevices.enumerateDevices().then(devices => {
                    stream.getTracks().forEach(t => t.stop()); // the stream is needed only for the permission
                    console.info('[PV] PhotonVoice_WebRTC_EnumerateDevices all:', kindStr, devices);
                    
                    // [id1, lbl1, id2, lbl2,...]
                    const res = devices.filter(d => d.kind == kindStr).map(d => [d.deviceId, d.label]).flat();
                    
                    console.info('[PV] PhotonVoice_WebRTC_EnumerateDevices res:', res);

                    // at least 1 slot: _malloc(0) may return null, which is not an out of memory for an empty list
                    const ptr = _malloc(Math.max(res.length, 1) * 4) >>> 0; // above 2GB, the i32 is negative in js
                    if (!ptr) {
                        console.error('[PV] PhotonVoice_WebRTC_EnumerateDevices: out of memory, can\'t allocate ' + (res.length * 4) + ' bytes (heap ' + (HEAPU8.length >>> 20) + ' MB), reports error 4');
                        {{{ makeDynCall('viiii', 'resultCallback') }}}(requestId, 4, 0, 0);
                        return;
                    }
                    let n = 0; // strings allocated
                    try {
                        for (; n < res.length; n++) {
                            const s = res[n];
                            const l = s.length * 4 + 1;
                            const sPtr = _malloc(l) >>> 0;
                            if (!sPtr) {
                                console.error('[PV] PhotonVoice_WebRTC_EnumerateDevices: out of memory, can\'t allocate ' + l + ' bytes (heap ' + (HEAPU8.length >>> 20) + ' MB), reports error 4');
                                break;
                            }
                            stringToUTF8(s, sPtr, l)
                            HEAPU32[ptr / 4 + n] = sPtr;
                        }

                        if (n == res.length) {
                            {{{ makeDynCall('viiii', 'resultCallback') }}}(requestId, 0, ptr, res.length);
                        } else {
                            {{{ makeDynCall('viiii', 'resultCallback') }}}(requestId, 4, 0, 0);
                        }
                    } finally {
                        // also if the callback throws
                        for (let i = 0; i < n; i++) {
                            _free(HEAPU32[ptr / 4 + i]);
                        }
                        _free(ptr);
                    }
                })
                .catch(function(err) {
                    stream.getTracks().forEach(t => t.stop());
                    console.error('[PV] PhotonVoice_WebRTC_EnumerateDevices enumerateDevices error: ' + err);
                    {{{ makeDynCall('viiii', 'resultCallback') }}}(requestId, 1, 0, 0);
                });
            })
            .catch(function(err) {
                console.error('[PV] PhotonVoice_WebRTC_EnumerateDevices enumerateDevices permission error: ' + err);
                {{{ makeDynCall('viiii', 'resultCallback') }}}(requestId, 2, 0, 0);
            });
        } else {
            console.error('[PV] PhotonVoice_WebRTC_EnumerateDevices error: ' + 'enumerateDevices not supported on your browser!');
            {{{ makeDynCall('viiii', 'resultCallback') }}}(requestId, 3, 0, 0);
        }
    },
});