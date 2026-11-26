mergeInto(LibraryManager.library, {
    
    PhotonVoice_JS_start_worker: function(encoderDataCallback, decoderDataCallback) {
        console.log('[PV] PhotonVoice_JS_start_worker');
        Module.PhotonVoice_JS_Global = {};
        Module.PhotonVoice_JS_Global.encoderChannels = new Map(); // number of channels required to properly allocate Float32Array in PhotonVoice_JS_opus_encode_float()
        Module.PhotonVoice_JS_Global.workerInitialized = false;
        Module.PhotonVoice_JS_Global.workerPreInitQueue = new Array();
        Module.PhotonVoice_JS_Global.opusWorker = new Worker("_opus_worker/opus_worker.js");
        Module.PhotonVoice_JS_Global.opusWorker.onmessage = (e) => {
            if (e.data.error) {
                console.error("[PV] OpusWorker", e.data.error);
            } else if (e.data.workerInitialized) {
                console.log('[PV] OpusWorker initialized');
                Module.PhotonVoice_JS_Global.workerInitialized = true;
//                Module.PhotonVoice_JS_Global.opusWorker.postMessage({op:"test"});

                for (let i = 0; i < Module.PhotonVoice_JS_Global.workerPreInitQueue.length; i++) {
                    Module.PhotonVoice_JS_Global.opusWorker.postMessage(Module.PhotonVoice_JS_Global.workerPreInitQueue[i]);
                }
                Module.PhotonVoice_JS_Global.workerPreInitQueue = new Array();
            } else {
                const b = e.data.packet;
                const ptr = _malloc(b.byteLength) >>> 0; // above 2GB, the i32 is negative in js
                if (!ptr) {
                    console.error('[PV] OpusWorker: out of memory, can\'t allocate ' + b.byteLength + ' bytes (heap ' + (HEAPU8.length >>> 20) + ' MB), ' + (e.data.decode ? 'decoded frame' : 'encoded packet') + ' dropped');
                    return;
                }
                try {
                    if (e.data.decode) {
                        HEAPF32.set(b, ptr / 4);
                        {{{ makeDynCall('viiii', 'decoderDataCallback') }}}(e.data.stream, ptr, b.length, e.data.eos);
                    } else {
                        HEAPU8.set(b, ptr);
                        {{{ makeDynCall('viii', 'encoderDataCallback') }}}(e.data.stream, ptr, b.length);
                    }
                } finally {
                    _free(ptr); // also if the callback throws
                }
            }
        };

    },
    

    PhotonVoice_JS_opus_encoder_init: function(st, f, channels, application) {
        console.log('[PV] PhotonVoice_JS_opus_encoder_init', st, f, channels, application);
        Module.PhotonVoice_JS_Global.encoderChannels.set(st, channels);
        const msg = {
                op: 'create_encoder',
                stream: st,
                sampleRate: f,
                numChannels: channels,
                application: application
            };
        if (Module.PhotonVoice_JS_Global.workerInitialized) {
            Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
        }
        else {
            Module.PhotonVoice_JS_Global.workerPreInitQueue.push(msg);
        }
        return 0;
    },

    PhotonVoice_JS_opus_encoder_destroy: function(st) {
        console.log('[PV] PhotonVoice_JS_opus_encoder_destroy', st);
        Module.PhotonVoice_JS_Global.encoderChannels.delete(st);
        const msg = {
                op: 'destroy',
                stream: st,
            };
        if (Module.PhotonVoice_JS_Global.workerInitialized) {
            Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
        }
        else {
            Module.PhotonVoice_JS_Global.workerPreInitQueue.push(msg);
        }
        return 0;
    },
    
    // c# expects a pointer to a static string
    PhotonVoice_JS_opus_get_version_string: function() {
        if (!Module.PhotonVoice_JS_VersionString) {
            const s = "OpusWorker";
            const ptr = _malloc(s.length + 1) >>> 0;
            if (!ptr) {
                console.error('[PV] PhotonVoice_JS_opus_get_version_string: out of memory, can\'t allocate ' + (s.length + 1) + ' bytes (heap ' + (HEAPU8.length >>> 20) + ' MB), returns null');
                return 0;
            }
            stringToUTF8(s, ptr, s.length + 1);
            Module.PhotonVoice_JS_VersionString = ptr;
        }
        return Module.PhotonVoice_JS_VersionString;
    },

    // the functions below are imported by OpusWrapper.cs and have to resolve at link time

    PhotonVoice_JS_opus_strerror: function(error) {
        const msgs = ["success", "invalid argument", "buffer too small", "internal error", "corrupted stream", "request not implemented", "invalid state", "memory allocation failed"];
        const s = msgs[-error] || "unknown error";
        Module.PhotonVoice_JS_ErrorStrings = Module.PhotonVoice_JS_ErrorStrings || {};
        if (!Module.PhotonVoice_JS_ErrorStrings[s]) {
            const ptr = _malloc(s.length + 1) >>> 0;
            if (!ptr) {
                console.error('[PV] PhotonVoice_JS_opus_strerror: out of memory, can\'t allocate ' + (s.length + 1) + ' bytes (heap ' + (HEAPU8.length >>> 20) + ' MB), returns null');
                return 0;
            }
            stringToUTF8(s, ptr, s.length + 1);
            Module.PhotonVoice_JS_ErrorStrings[s] = ptr;
        }
        return Module.PhotonVoice_JS_ErrorStrings[s];
    },

    // ported from opus_packet_get_bandwidth()
    PhotonVoice_JS_opus_packet_get_bandwidth: function(data) {
        if (!data) return -1; // OPUS_BAD_ARG
        const toc = HEAPU8[data >>> 0];
        if (toc & 0x80) {
            const bandwidth = 1102 + ((toc >> 5) & 0x3); // Mediumband...
            return bandwidth == 1102 ? 1101 : bandwidth; // Narrowband
        } else if ((toc & 0x60) == 0x60) {
            return (toc & 0x10) ? 1105 : 1104; // Fullband : SuperWideband
        } else {
            return 1101 + ((toc >> 5) & 0x3); // Narrowband...
        }
    },

    // ported from opus_packet_get_nb_channels()
    PhotonVoice_JS_opus_packet_get_nb_channels: function(data) {
        if (!data) return -1; // OPUS_BAD_ARG
        return (HEAPU8[data >>> 0] & 0x4) ? 2 : 1;
    },

    // not used: the worker keeps the encoder / decoder state
    PhotonVoice_JS_opus_encoder_get_size: function(channels) {
        return 0;
    },

    PhotonVoice_JS_opus_decoder_get_size: function(channels) {
        return 0;
    },

    PhotonVoice_JS_opus_encode: function(st, pcm, frame_size, data, max_data_bytes) {
        if (!Module.PhotonVoice_JS_Global.workerInitialized) return;
        pcm >>>= 0; // above 2GB, the i32 is negative in js
        const msg = {
            op: 'encode',
            stream: st,
            frame: new Int16Array(HEAP16.buffer.slice(pcm, pcm + frame_size * 2 * Module.PhotonVoice_JS_Global.encoderChannels.get(st)))
        };
        Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
    },

    PhotonVoice_JS_opus_encode_float: function(st, pcm, frame_size, data, max_data_bytes) {
        if (!Module.PhotonVoice_JS_Global.workerInitialized) return;
        pcm >>>= 0;
        const msg = {
            op: 'encode_float',
            stream: st,
            frame: new Float32Array(HEAPF32.buffer.slice(pcm, pcm + frame_size * 4 * Module.PhotonVoice_JS_Global.encoderChannels.get(st)))
        };
        Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
    },

    PhotonVoice_JS_opus_encoder_ctl_set: function(st, request, value) {
        const msg = {
                op: 'ctl_set',
                stream: st,
                request: request,
                value: value
            };
        if (Module.PhotonVoice_JS_Global.workerInitialized) {
            Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
        }
        else {
            Module.PhotonVoice_JS_Global.workerPreInitQueue.push(msg);
        }
        return 0;
    },

    // not used, it's not possible to implement synchronously
    PhotonVoice_JS_opus_encoder_ctl_get: function(st, request, value) {
        return 0;
    },

    PhotonVoice_JS_opus_decoder_init: function(st, f, channels) {
        console.log('[PV] PhotonVoice_JS_opus_decoder_init', st, f, channels);
        const msg = {
                op: 'create_decoder',
                stream: st,
                sampleRate: f,
                numChannels: channels,
            };
        if (Module.PhotonVoice_JS_Global.workerInitialized) {
            Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
        }
        else {
            Module.PhotonVoice_JS_Global.workerPreInitQueue.push(msg);
        }
        return 0;
    },

    PhotonVoice_JS_opus_decode_async: function(st, data, len, fec, eos) {
        if (!Module.PhotonVoice_JS_Global.workerInitialized) return;
        data >>>= 0;
        const msg = {
            op: 'decode',
            stream: st,
            frame: new Int8Array(HEAPF32.buffer.slice(data, data + len)),
            fec: fec,
            eos: eos
        };
        Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
    },

    PhotonVoice_JS_opus_decode_float_async: function(st, data, len, fec, eos) {
        if (!Module.PhotonVoice_JS_Global.workerInitialized) return;
        data >>>= 0;
        const msg = {
            op: 'decode_float',
            stream: st,
            frame: new Int8Array(HEAPF32.buffer.slice(data, data + len)),
            fec: fec,
            eos: eos
        };
        Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
    },
    
    PhotonVoice_JS_opus_decoder_ctl_set: function(st, request, value) {
        const msg = {
                op: 'ctl_set',
                stream: st,
                request: request,
                value: value
            };
        if (Module.PhotonVoice_JS_Global.workerInitialized) {
            Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
        }
        else {
            Module.PhotonVoice_JS_Global.workerPreInitQueue.push(msg);
        }
        return 0;
    },
    
    // not used, it's not possible to implement synchronously
    PhotonVoice_JS_opus_decoder_ctl_get: function(st, request, value) {
        return 0;
    },
    
    PhotonVoice_JS_opus_decoder_destroy: function(st) {
        console.log('[PV] PhotonVoice_JS_opus_decoder_destroy', st);
        const msg = {
                op: 'destroy',
                stream: st,
            };
        if (Module.PhotonVoice_JS_Global.workerInitialized) {
            Module.PhotonVoice_JS_Global.opusWorker.postMessage(msg);
        }
        else {
            Module.PhotonVoice_JS_Global.workerPreInitQueue.push(msg);
        }
        return 0;
    }
});