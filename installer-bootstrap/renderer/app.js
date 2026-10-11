(() => {
  'use strict';

  const $ = (sel) => document.querySelector(sel);
  const $$ = (sel) => Array.from(document.querySelectorAll(sel));

  const state = {
    info: null,
    dir: '',
    accepted: false,
    installStart: 0,
    lastFiles: 0,
    timer: null
  };

  // ---------- Quantum field background ----------

  const canvas = $('#field');
  const ctx = canvas.getContext('2d');
  let W = 0, H = 0, DPR = Math.min(window.devicePixelRatio || 1, 2);
  let nodes = [];
  let flashes = [];

  function resize() {
    W = window.innerWidth;
    H = window.innerHeight;
    canvas.width = Math.floor(W * DPR);
    canvas.height = Math.floor(H * DPR);
    canvas.style.width = W + 'px';
    canvas.style.height = H + 'px';
    ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
    const count = Math.round(Math.min(90, Math.max(40, (W * H) / 16000)));
    nodes = Array.from({ length: count }, () => ({
      x: Math.random() * W,
      y: Math.random() * H,
      vx: (Math.random() - 0.5) * 0.25,
      vy: (Math.random() - 0.5) * 0.25,
      r: Math.random() * 1.6 + 0.4,
      hue: Math.random() < 0.55 ? 'm' : 'p',
      phase: Math.random() * Math.PI * 2
    }));
  }

  function spawnFlash() {
    flashes.push({
      x: Math.random() * W,
      y: Math.random() * H,
      life: 0,
      max: 90 + Math.random() * 60,
      radius: 40 + Math.random() * 90
    });
  }

  function frame(t) {
    ctx.clearRect(0, 0, W, H);

    for (const n of nodes) {
      n.x += n.vx;
      n.y += n.vy;
      if (n.x < -10) n.x = W + 10;
      if (n.x > W + 10) n.x = -10;
      if (n.y < -10) n.y = H + 10;
      if (n.y > H + 10) n.y = -10;
    }

    // connections
    for (let i = 0; i < nodes.length; i++) {
      const a = nodes[i];
      for (let j = i + 1; j < nodes.length; j++) {
        const b = nodes[j];
        const dx = a.x - b.x, dy = a.y - b.y;
        const d2 = dx * dx + dy * dy;
        if (d2 < 15000) {
          const alpha = (1 - d2 / 15000) * 0.22;
          const magenta = a.hue === 'm' || b.hue === 'm';
          ctx.strokeStyle = magenta
            ? `rgba(255,43,214,${alpha})`
            : `rgba(138,43,255,${alpha})`;
          ctx.lineWidth = 0.6;
          ctx.beginPath();
          ctx.moveTo(a.x, a.y);
          ctx.lineTo(b.x, b.y);
          ctx.stroke();
        }
      }
    }

    // nodes
    for (const n of nodes) {
      const pulse = 0.6 + Math.sin(t / 900 + n.phase) * 0.4;
      const color = n.hue === 'm' ? '255,43,214' : '177,92,255';
      ctx.beginPath();
      ctx.fillStyle = `rgba(${color},${0.55 * pulse})`;
      ctx.shadowColor = `rgba(${color},.9)`;
      ctx.shadowBlur = 10;
      ctx.arc(n.x, n.y, n.r * (0.8 + pulse * 0.4), 0, Math.PI * 2);
      ctx.fill();
      ctx.shadowBlur = 0;
    }

    // flashes (quantum collapse rings)
    flashes = flashes.filter((f) => f.life < f.max);
    for (const f of flashes) {
      f.life++;
      const k = f.life / f.max;
      const r = f.radius * k;
      ctx.beginPath();
      ctx.strokeStyle = `rgba(255,43,214,${(1 - k) * 0.5})`;
      ctx.lineWidth = 1.2;
      ctx.arc(f.x, f.y, r, 0, Math.PI * 2);
      ctx.stroke();
      ctx.beginPath();
      ctx.strokeStyle = `rgba(138,43,255,${(1 - k) * 0.35})`;
      ctx.arc(f.x, f.y, r * 0.62, 0, Math.PI * 2);
      ctx.stroke();
    }

    requestAnimationFrame(frame);
  }

  window.addEventListener('resize', resize);
  resize();
  requestAnimationFrame(frame);
  setInterval(() => { if (Math.random() < 0.6) spawnFlash(); }, 1600);

  // ---------- Navigation ----------

  const order = ['welcome', 'license', 'location', 'install', 'done'];
  let current = 'welcome';

  function show(name) {
    const from = $(`#screen-${current}`);
    const to = $(`#screen-${name}`);
    if (!to) return;
    if (from && from !== to) {
      from.classList.remove('active');
      from.classList.add('leave-left');
      setTimeout(() => from.classList.remove('leave-left'), 600);
    }
    to.classList.add('active');
    to.classList.remove('leave-left');
    current = name;
    updateRail(name);
  }

  function updateRail(name) {
    const idx = order.indexOf(name);
    $$('#steps li').forEach((li) => {
      const i = order.indexOf(li.dataset.step);
      li.classList.toggle('active', i === idx);
      li.classList.toggle('done', i < idx);
    });
  }

  $$('[data-back]').forEach((btn) => btn.addEventListener('click', () => show(btn.dataset.back)));

  // ---------- Window controls ----------

  $('#btn-min').addEventListener('click', () => window.qv.minimise());
  $('#btn-close').addEventListener('click', () => window.qv.close());

  // ---------- Welcome ----------

  $('#btn-begin').addEventListener('click', async () => {
    await loadEula();
    show('license');
  });

  // ---------- License ----------

  async function loadEula() {
    const box = $('#eula-text');
    if (box.dataset.loaded) return;
    box.textContent = await window.qv.eula();
    box.dataset.loaded = '1';
    box.scrollTop = 0;
    checkEulaScroll();
  }

  function checkEulaScroll() {
    const box = $('#eula-text');
    const atEnd = box.scrollTop + box.clientHeight >= box.scrollHeight - 12;
    if (atEnd || box.scrollHeight <= box.clientHeight + 4) {
      $('#accept-box').disabled = false;
      $('#eula-hint').textContent = 'Tick the box to accept the agreement.';
    }
  }

  $('#eula-text').addEventListener('scroll', checkEulaScroll);

  $('#accept-box').addEventListener('change', (e) => {
    state.accepted = e.target.checked;
    $('#btn-accept').disabled = !state.accepted;
  });

  $('#btn-accept').addEventListener('click', () => {
    if (!state.accepted) return;
    show('location');
    $('#dir').focus();
  });

  // ---------- Location ----------

  async function initInfo() {
    state.info = await window.qv.info();
    $('#ver').textContent = 'v' + state.info.version;
    $('#dir').value = state.info.defaultDir;
    state.dir = state.info.defaultDir;
    if (!state.info.payloadFound) {
      showError('Setup is incomplete', 'The installer payload is missing from this bootstrapper. Download the setup again from the official release page.');
    }
  }

  function setDirError(msg) {
    $('#dir-error').textContent = msg || '';
    $('#dir').classList.toggle('invalid', !!msg);
  }

  let validateTimer = null;
  $('#dir').addEventListener('input', () => {
    clearTimeout(validateTimer);
    validateTimer = setTimeout(async () => {
      const value = $('#dir').value.trim();
      state.dir = value;
      const res = await window.qv.validateDir(value);
      setDirError(res.ok ? '' : res.reason);
    }, 180);
  });

  $('#btn-browse').addEventListener('click', async () => {
    const chosen = await window.qv.browse($('#dir').value.trim());
    if (chosen) {
      $('#dir').value = chosen;
      state.dir = chosen;
      setDirError('');
    }
  });

  $('#btn-install').addEventListener('click', async () => {
    const value = $('#dir').value.trim();
    const res = await window.qv.validateDir(value);
    if (!res.ok) {
      setDirError(res.reason);
      $('#dir').focus();
      return;
    }
    state.dir = value;
    startInstall();
  });

  // ---------- Install ----------

  const tickerLines = [];
  function tick(text, hot) {
    const box = $('#ticker');
    const line = document.createElement('span');
    line.className = 'line' + (hot ? ' hot' : '');
    line.textContent = text;
    box.appendChild(line);
    tickerLines.push(line);
    while (tickerLines.length > 4) {
      const old = tickerLines.shift();
      old.remove();
    }
    box.scrollTop = box.scrollHeight;
  }

  function setPhase(label) {
    $('#install-phase').textContent = label;
    tick(label, true);
  }

  function startTimer() {
    state.installStart = Date.now();
    clearInterval(state.timer);
    state.timer = setInterval(() => {
      const s = Math.floor((Date.now() - state.installStart) / 1000);
      $('#stat-elapsed').textContent = s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`;
    }, 250);
  }

  function stopTimer() { clearInterval(state.timer); }

  function startInstall() {
    show('install');
    $('#install-title').textContent = 'Installing';
    $('#install-note').textContent = 'Please keep this window open.';
    $('#pct').textContent = 'Working';
    $('#ticker').innerHTML = '';
    tickerLines.length = 0;
    state.lastFiles = 0;
    $('#stat-files').textContent = '0';
    $('#stat-steps').textContent = '0';
    const bar = $('#bar');
    bar.style.width = '';
    bar.classList.add('indeterminate');
    startTimer();

    let steps = 0;
    setPhase('Preparing the installation');
    steps++;

    window.qv.install(state.dir).then((res) => {
      stopTimer();
      bar.classList.remove('indeterminate');
      if (res.ok) {
        bar.style.width = '100%';
        $('#pct').textContent = '100%';
        setPhase('Installation complete');
        setTimeout(finish, 900);
      } else {
        showError('Installation did not complete', res.message || 'The installer stopped unexpectedly.');
      }
    });
  }

  window.qv.onPhase((data) => {
    $('#install-phase').textContent = data.label;
    tick(data.label, true);
  });

  window.qv.onProgress((data) => {
    const files = data.copied || 0;
    if (files !== state.lastFiles) {
      state.lastFiles = files;
      $('#stat-files').textContent = String(files);
      $('#stat-steps').textContent = String(data.created || 0);
      tick(`Copied ${files} file${files === 1 ? '' : 's'}`, false);
      $('#install-phase').textContent = 'Copying application files';
    }
  });

  // ---------- Finish ----------

  function finish() {
    $('#done-path').textContent = state.dir;
    show('done');
  }

  $('#btn-finish').addEventListener('click', async () => {
    if ($('#launch-box').checked) {
      await window.qv.launch(state.dir);
    }
    window.qv.close();
  });

  $('#btn-log').addEventListener('click', () => window.qv.openLog());

  // ---------- Error ----------

  function showError(title, message) {
    stopTimer();
    $('#error-title').textContent = title;
    $('#error-msg').textContent = message;
    show('error');
  }

  $('#btn-error-log').addEventListener('click', () => window.qv.openLog());
  $('#btn-retry').addEventListener('click', () => show('location'));

  // ---------- Keyboard ----------

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      if (current === 'welcome') $('#btn-begin').click();
      else if (current === 'license' && !$('#btn-accept').disabled) $('#btn-accept').click();
      else if (current === 'location') $('#btn-install').click();
      else if (current === 'done') $('#btn-finish').click();
    }
    if (e.key === 'Escape' && current === 'license') show('welcome');
  });

  initInfo();
  show('welcome');
})();
