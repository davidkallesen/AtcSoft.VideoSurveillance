window.hlsInterop = {
    _players: {},

    initPlayer: function (elementId, streamUrl) {
        this.destroyPlayer(elementId);

        var video = document.getElementById(elementId);
        if (!video) {
            console.warn('[hlsInterop] Element not found:', elementId);
            return;
        }

        // An element that is marked muted starts muted. The attribute alone
        // does not do it for an element the page adds later, and a browser
        // refuses to start a stream with sound by itself unless it is muted.
        // The viewer turns the sound on with the player's own control.
        if (video.hasAttribute('muted')) {
            video.muted = true;
        }

        if (typeof Hls !== 'undefined' && Hls.isSupported()) {
            var hls = new Hls({
                enableWorker: true,
                lowLatencyMode: true
            });
            hls.loadSource(streamUrl);
            hls.attachMedia(video);
            hls.on(Hls.Events.MANIFEST_PARSED, function () {
                video.play().catch(function () { });
            });
            hls.on(Hls.Events.ERROR, function (event, data) {
                if (data.fatal) {
                    console.error('[hlsInterop] Fatal error:', data.type, data.details);
                    if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
                        hls.startLoad();
                    } else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) {
                        hls.recoverMediaError();
                    }
                }
            });
            this._players[elementId] = hls;
        } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
            // Native HLS support (Safari)
            video.src = streamUrl;
            video.addEventListener('loadedmetadata', function () {
                video.play().catch(function () { });
            });
            this._players[elementId] = 'native';
        } else {
            console.error('[hlsInterop] HLS is not supported in this browser.');
        }
    },

    // The size of the picture a video element plays: [width, height],
    // zeros until the first picture has arrived.
    videoSize: function (elementId) {
        var video = document.getElementById(elementId);
        return video ? [video.videoWidth || 0, video.videoHeight || 0] : [0, 0];
    },

    destroyPlayer: function (elementId) {
        var player = this._players[elementId];
        if (!player) {
            return;
        }
        if (player !== 'native' && typeof player.destroy === 'function') {
            player.destroy();
        }
        delete this._players[elementId];
    }
};

window.fileInterop = {
    downloadBlob: function (base64Data, fileName, contentType) {
        var byteCharacters = atob(base64Data);
        var byteNumbers = new Array(byteCharacters.length);
        for (var i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        var byteArray = new Uint8Array(byteNumbers);
        var blob = new Blob([byteArray], { type: contentType });

        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    downloadFromUrl: function (url, fileName) {
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    }
};
window.recordingTimeline = {
    // Where the player is and how long the recording is, in seconds.
    position: function (videoId) {
        var video = document.getElementById(videoId);
        return video && isFinite(video.duration) ? [video.currentTime, video.duration] : [0, 0];
    },

    seekTo: function (videoId, seconds) {
        var video = document.getElementById(videoId);
        if (video && isFinite(video.duration)) {
            video.currentTime = Math.max(0, Math.min(seconds, video.duration));
        }
    },

    // A click on the timeline jumps to the same place in the recording.
    seekAt: function (videoId, element, clientX) {
        var video = document.getElementById(videoId);
        if (!video || !element || !isFinite(video.duration)) {
            return;
        }

        var box = element.getBoundingClientRect();
        if (box.width > 0) {
            var fraction = Math.max(0, Math.min(1, (clientX - box.left) / box.width));
            video.currentTime = fraction * video.duration;
        }
    }
};

window.timelineInterop = {
    // Where on the element a click landed, as a fraction of its width.
    fractionAt: function (elementId, clientX) {
        var element = document.getElementById(elementId);
        if (!element) {
            return 0;
        }

        var box = element.getBoundingClientRect();
        return box.width > 0 ? Math.max(0, Math.min(1, (clientX - box.left) / box.width)) : 0;
    },

    // Tells the dialog when a recording has played to its end, so it can move on to the next.
    onEnded: function (videoId, dotNetRef) {
        var video = document.getElementById(videoId);
        if (video) {
            video.addEventListener('ended', function () { dotNetRef.invokeMethodAsync('OnEndedAsync'); });
        }
    },

    // Starts a recording this far in, once the player knows its length.
    startAt: function (videoId, seconds) {
        var video = document.getElementById(videoId);
        if (!video) {
            return;
        }

        var seek = function () { video.currentTime = Math.min(seconds, isFinite(video.duration) ? video.duration : seconds); };
        if (video.readyState >= 1) {
            seek();
        } else {
            video.addEventListener('loadedmetadata', seek, { once: true });
        }
    }
};