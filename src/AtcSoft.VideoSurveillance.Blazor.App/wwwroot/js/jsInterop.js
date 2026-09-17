window.hlsInterop = {
    _players: {},

    initPlayer: function (elementId, streamUrl) {
        this.destroyPlayer(elementId);

        var video = document.getElementById(elementId);
        if (!video) {
            console.warn('[hlsInterop] Element not found:', elementId);
            return;
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