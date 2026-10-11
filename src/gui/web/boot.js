// Loads before app.js. If app.js fails to parse or throws, the reason is shown in the banner instead of a blank window.
(function () {
  function show(msg) {
    const b = document.getElementById('boot-error');
    if (b) { b.textContent = msg; b.classList.remove('hidden'); }
  }
  window.addEventListener('error', (e) =>
    show('Interface error: ' + e.message + (e.lineno ? ' (line ' + e.lineno + ')' : '')));
  window.addEventListener('unhandledrejection', (e) =>
    show('Interface error: ' + ((e.reason && e.reason.message) || e.reason)));
})();
