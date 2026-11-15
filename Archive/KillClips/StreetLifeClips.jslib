// Kill clips (match chat): the browser build saves a clip the way the web game does, by playing its JPEG frames onto
// a canvas and recording that with MediaRecorder, then downloading the .webm (or .mp4 where that's what the browser
// records). Called from KillClips.cs (ClipSaver.WebJob).
mergeInto(LibraryManager.library, {
  SLClipBegin: function (fps) {
    Module.slClip = { fps: fps, parts: [] };
    Module.slClipState = 'working:0';
  },
  SLClipFrame: function (ptr, len) {
    var a = HEAPU8.slice(ptr, ptr + len);
    Module.slClip.parts.push(new Blob([a], { type: 'image/jpeg' }));
  },
  SLClipEnd: function (namePtr) {
    var name = UTF8ToString(namePtr), C = Module.slClip;
    if (!window.MediaRecorder || !HTMLCanvasElement.prototype.captureStream || !window.createImageBitmap) { Module.slClipState = 'error:this browser can\'t record video'; return; }
    Promise.all(C.parts.map(function (b) { return createImageBitmap(b); })).then(function (imgs) {
      if (!imgs.length) { Module.slClipState = 'error:empty clip'; return; }
      var cv = document.createElement('canvas'); cv.width = imgs[0].width; cv.height = imgs[0].height;
      var g = cv.getContext('2d'); g.drawImage(imgs[0], 0, 0);
      var types = ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm', 'video/mp4'], type = '';
      for (var k = 0; k < types.length; k++) if (MediaRecorder.isTypeSupported(types[k])) { type = types[k]; break; }
      var rec = new MediaRecorder(cv.captureStream(C.fps), type ? { mimeType: type, videoBitsPerSecond: 8000000 } : undefined), parts = [];
      rec.ondataavailable = function (e) { if (e.data && e.data.size) parts.push(e.data); };
      rec.onstop = function () {
        var mt = rec.mimeType || 'video/webm', blob = new Blob(parts, { type: mt });
        var a = document.createElement('a'); a.href = URL.createObjectURL(blob);
        a.download = name + (mt.indexOf('mp4') >= 0 ? '.mp4' : '.webm');
        document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 2000);
        for (var j = 0; j < imgs.length; j++) imgs[j].close && imgs[j].close();
        Module.slClipState = 'done';
      };
      rec.start(200);
      var i = 0, iv = setInterval(function () {
        if (i >= imgs.length) { clearInterval(iv); setTimeout(function () { try { rec.requestData(); rec.stop(); } catch (e) { Module.slClipState = 'error:' + e; } }, 300); return; }
        g.drawImage(imgs[i++], 0, 0);
        Module.slClipState = 'working:' + (i / imgs.length).toFixed(3);
      }, 1000 / C.fps);
    }).catch(function (e) { Module.slClipState = 'error:' + e; });
  },
  SLClipState: function () {
    var s = Module.slClipState || '', n = lengthBytesUTF8(s) + 1, p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  }
});
