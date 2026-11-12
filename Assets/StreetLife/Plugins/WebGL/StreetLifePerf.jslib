// Lets the comparison page read the Unity build's numbers: window.slPerf is refreshed twice a second, and the parent
// page (the side-by-side comparison) gets a message when the match scene is ready.
mergeInto(LibraryManager.library, {
  SL_Perf: function (json) {
    try { window.slPerf = JSON.parse(UTF8ToString(json)); } catch (e) {}
  },
  SL_Ready: function (secs) {
    window.slReadyAt = performance.now();
    window.slUnityReady = secs;
    try { if (window.parent && window.parent !== window) window.parent.postMessage({ sl: 'ready', t: window.slReadyAt }, '*'); } catch (e) {}
  }
});
