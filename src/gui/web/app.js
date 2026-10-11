(() => {
  'use strict';

  // ── helpers ────────────────────────────────────────────────────────────
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const ago = (iso) => {
    const d = (Date.now() - new Date(iso).getTime()) / 1000;
    if (d < 60) return 'just now';
    if (d < 3600) return Math.floor(d / 60) + 'm ago';
    if (d < 86400) return Math.floor(d / 3600) + 'h ago';
    if (d < 604800) return Math.floor(d / 86400) + 'd ago';
    return new Date(iso).toLocaleDateString();
  };
  const when = (iso) => new Date(iso).toLocaleString([], { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

  let toastTimer;
  function toast(msg) {
    const t = $('#toast');
    t.textContent = msg;
    t.classList.remove('hidden');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.add('hidden'), 4500);
  }

  // ── bridge to C# ───────────────────────────────────────────────────────
  const pending = new Map();
  let seq = 0;

  function call(cmd, args = {}) {
    return new Promise((resolve, reject) => {
      const id = ++seq;
      pending.set(String(id), { resolve, reject });
      window.chrome.webview.postMessage({ id, cmd, args });
    });
  }

  window.chrome.webview.addEventListener('message', (e) => {
    const m = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
    if (m.event === 'token') {
      if (streamEl) { streamEl.textContent += m.text; $('#chat').scrollTop = $('#chat').scrollHeight; }
      return;
    }
    if (m.event === 'download') {
      $('#model-state').textContent = 'Downloading model… ' + m.pct + '%';
      return;
    }
    if (m.event === 'reminder') {
      toast('⏰ Reminder: ' + m.text);
      if (current === 'assistant') loaders.assistant();
      return;
    }
    const p = pending.get(String(m.id));
    if (!p) return;
    pending.delete(String(m.id));
    if (m.ok) p.resolve(m.data);
    else { const err = new Error(m.error); err.code = m.code; p.reject(err); }
  });

  // ── modal (generic form) ───────────────────────────────────────────────
  function modal({ title, fields = [], okText = 'OK' }) {
    return new Promise((resolve) => {
      $('#m-title').textContent = title;
      $('#m-body').innerHTML = fields.map(f =>
        `<label class="field"><span>${esc(f.label)}</span>` +
        `<input class="input" data-k="${esc(f.name)}" type="${esc(f.type || 'text')}" value="${esc(f.value || '')}" autocomplete="off"></label>`
      ).join('');
      $('#m-ok').textContent = okText;
      $('#modal').classList.remove('hidden');
      const first = $('#m-body input');
      if (first) first.focus();

      const finish = (value) => {
        $('#modal').classList.add('hidden');
        $('#m-ok').onclick = $('#m-cancel').onclick = null;
        document.removeEventListener('keydown', onKey);
        resolve(value);
      };
      const submit = () => {
        const out = {};
        $$('#m-body input').forEach(i => out[i.dataset.k] = i.value);
        finish(out);
      };
      const onKey = (e) => {
        if (e.key === 'Escape') finish(null);
        if (e.key === 'Enter' && !e.target.matches('textarea')) submit();
      };
      $('#m-ok').onclick = submit;
      $('#m-cancel').onclick = () => finish(null);
      document.addEventListener('keydown', onKey);
    });
  }

  // Asks for the notes password. Returns true if unlocked.
  async function unlockNotes() {
    const v = await modal({ title: 'Notes password', okText: 'Unlock',
      fields: [{ name: 'password', label: 'Master password', type: 'password' }] });
    if (!v) return false;
    try { await call('unlock', { password: v.password }); toast('Notes unlocked'); return true; }
    catch (err) { toast(err.message); return false; }
  }

  // Runs an action; if notes are locked, prompts for the password and retries once.
  async function withUnlock(action) {
    try { return await action(); }
    catch (err) {
      if (err.code !== 'locked') throw err;
      if (!(await unlockNotes())) return undefined;
      return await action();
    }
  }

  // ── routing ────────────────────────────────────────────────────────────
  let current = 'dashboard';
  const loaders = {};

  function show(page) {
    current = page;
    $$('.page').forEach(p => p.classList.toggle('active', p.id === 'page-' + page));
    $$('.nav-btn').forEach(b => b.classList.toggle('active', b.dataset.page === page));
    if (loaders[page]) loaders[page]().catch(e => toast(e.message));
  }

  // ── dashboard ──────────────────────────────────────────────────────────
  loaders.dashboard = async () => {
    const s = await call('stats');
    $('#m-total').textContent = s.total;
    $('#m-notes').textContent = s.notes;
    $('#m-snippets').textContent = s.snippets;
    $('#m-commands').textContent = s.commands;
    const rec = await call('recent', { limit: 8 });
    $('#recent').innerHTML = rec.length
      ? rec.map(e => rowHtml(e)).join('')
      : '<p class="muted" style="padding:10px">No entries yet. Create your first one.</p>';
    $$('#recent .row-item').forEach(el => el.onclick = () => openFromList(el.dataset.id));
  };

  function rowHtml(e, selectedId) {
    return `<div class="row-item ${e.id === selectedId ? 'selected' : ''}" data-id="${esc(e.id)}">` +
      `<span class="badge ${esc(e.type)}">${esc(e.typeLabel)}</span>` +
      `<div class="grow" style="min-width:0"><div class="t">${esc(e.title)}</div>` +
      `<div class="s">${esc(e.description || e.language || '')}</div></div>` +
      `<span class="muted sm">${ago(e.updated)}</span></div>`;
  }

  // ── vault ──────────────────────────────────────────────────────────────
  let filter = 'all';
  let openId = null;
  let openEntry = null;
  let dirty = false;

  loaders.vault = async () => refreshVaultList();

  async function refreshVaultList() {
    const q = $('#vault-search').value.trim();
    const list = q ? await call('search', { q }) : await call('list', { section: filter });
    $('#vault-list').innerHTML = list.length
      ? list.map(e => rowHtml(e, openId)).join('')
      : '<p class="muted" style="padding:10px">Nothing here yet.</p>';
    $$('#vault-list .row-item').forEach(el => el.onclick = () => openFromList(el.dataset.id));
  }

  async function openFromList(id) {
    if (dirty && !(await saveCurrent(true))) return;
    if (current !== 'vault') show('vault');
    await openById(id);
  }

  async function openById(id) {
    const e = await withUnlock(() => call('get', { id }));
    if (!e) return;
    openId = e.id;
    openEntry = e;
    fillEditor(e);
    await refreshVaultList();
  }

  function fillEditor(e) {
    $('#editor-empty').classList.add('hidden');
    $('#editor-body').classList.remove('hidden');
    $('#ed-title').value = e.title;
    $('#ed-content').value = e.content;
    $('#ed-tags').value = e.tags || '';
    $('#ed-desc').value = e.description || '';
    $('#ed-lang').value = e.language || '';
    $('#ed-dpapi').checked = !!e.dpapi;
    $('#ed-dpapi-wrap').classList.toggle('hidden', e.type !== 'note');
    $('#ed-type').textContent = (e.typeLabel || e.type).toUpperCase();
    $('#ed-type').className = 'badge ' + e.type;
    $('#ed-fav').textContent = e.favorite ? '★' : '☆';
    $('#ed-updated').textContent = 'Updated ' + ago(e.updated);
    $('#ed-count').textContent = e.content.length + ' chars';
    $('#ed-saved').classList.add('hidden');
    dirty = false;
  }

  function collectEditor() {
    return {
      id: openId,
      title: $('#ed-title').value,
      content: $('#ed-content').value,
      tags: $('#ed-tags').value,
      description: $('#ed-desc').value,
      language: $('#ed-lang').value,
      dpapi: $('#ed-dpapi').checked
    };
  }

  async function saveCurrent(silent = false) {
    if (!openId) return true;
    try {
      await call('update', collectEditor());
      dirty = false;
      if (!silent) {
        $('#ed-saved').classList.remove('hidden');
        setTimeout(() => $('#ed-saved').classList.add('hidden'), 2000);
      }
      await refreshVaultList();
      return true;
    } catch (err) { toast(err.message); return false; }
  }

  function markDirty() {
    dirty = true;
    $('#ed-saved').classList.add('hidden');
    $('#ed-count').textContent = $('#ed-content').value.length + ' chars';
  }

  $$('#chips .chip').forEach(c => c.onclick = () => {
    filter = c.dataset.filter;
    $$('#chips .chip').forEach(x => x.classList.toggle('active', x === c));
    refreshVaultList().catch(e => toast(e.message));
  });
  $('#vault-search').addEventListener('input', () => refreshVaultList().catch(() => {}));
  ['#ed-title', '#ed-content', '#ed-tags', '#ed-desc', '#ed-lang'].forEach(s => $(s).addEventListener('input', markDirty));
  $('#ed-dpapi').addEventListener('change', markDirty);
  $('#ed-save').onclick = () => saveCurrent();

  $('#ed-copy').onclick = async () => {
    const text = $('#ed-content').value;
    try { await navigator.clipboard.writeText(text); }
    catch { const ta = $('#ed-content'); ta.select(); document.execCommand('copy'); }
    toast('Copied to clipboard');
  };

  $('#ed-fav').onclick = async () => {
    if (!openId) return;
    const next = !(openEntry && openEntry.favorite);
    await call('favorite', { id: openId, value: next });
    if (openEntry) openEntry.favorite = next;
    $('#ed-fav').textContent = next ? '★' : '☆';
    refreshVaultList();
  };

  $('#ed-dup').onclick = async () => {
    if (!openId) return;
    if (dirty) await saveCurrent(true);
    const nid = await call('duplicate', { id: openId });
    await refreshVaultList();
    await openById(nid);
    toast('Duplicated');
  };

  $('#ed-del').onclick = async () => {
    if (!openId) return;
    if (!confirm('Delete "' + $('#ed-title').value + '"? This cannot be undone.')) return;
    await call('delete', { id: openId });
    openId = null; openEntry = null; dirty = false;
    $('#editor-body').classList.add('hidden');
    $('#editor-empty').classList.remove('hidden');
    refreshVaultList();
    toast('Deleted');
  };

  // New entry (used by several buttons)
  async function newEntry() {
    const v = await modal({ title: 'New entry', okText: 'Create', fields: [
      { name: 'type', label: 'Type: note, snippet or command', value: 'snippet' }] });
    if (!v) return;
    const type = (v.type || '').trim().toLowerCase();
    if (!['note', 'snippet', 'command'].includes(type)) return toast('Type must be note, snippet or command.');
    const id = await withUnlock(() => call('create', { type }));
    if (!id) return;
    show('vault');
    filter = 'all';
    $$('#chips .chip').forEach(x => x.classList.toggle('active', x.dataset.filter === 'all'));
    await openById(id);
    $('#ed-title').focus();
    $('#ed-title').select();
  }

  // ── assistant ──────────────────────────────────────────────────────────
  function appendChat(who, text) {
    const log = $('#chat');
    log.insertAdjacentHTML('beforeend',
      `<div><span class="who ${who === 'Assistant' ? 'bot' : ''}">${esc(who)}</span>: ${esc(text)}\n\n</div>`);
    log.scrollTop = log.scrollHeight;
  }

  function renderReminders(list) {
    $('#reminders').innerHTML = list.length
      ? list.map(r => `<div class="rem ${r.done ? 'done' : ''}" data-id="${esc(r.id)}">` +
          `<span class="box"></span><span class="t">${esc(r.text)}</span><span class="d">${when(r.due)}</span></div>`).join('')
      : '<p class="muted" style="padding:10px">No reminders yet.</p>';
    $$('#reminders .rem').forEach(el => el.onclick = async () => {
      renderReminders(await call('doneReminder', { id: el.dataset.id }));
    });
  }

  async function refreshModel() {
    const s = await call('model.status');
    $('#model-state').textContent = s.installed
      ? (s.loaded ? 'Model ready: ' : 'Model installed: ') + s.name
      : 'Model not downloaded: ' + s.name + ' (about 0.4 GB)';
    $('#model-dl').classList.toggle('hidden', s.installed);
  }

  loaders.assistant = async () => {
    renderReminders(await call('reminders'));
    await refreshModel();
  };

  $('#model-dl').onclick = async () => {
    $('#model-dl').disabled = true;
    try { await call('model.download'); toast('Model downloaded'); }
    catch (err) { appendChat('Assistant', 'Download failed: ' + err.message); }
    $('#model-dl').disabled = false;
    refreshModel();
  };

  let streamEl = null;

  function startBubble(who) {
    const log = $('#chat');
    log.insertAdjacentHTML('beforeend', `<div><span class="who bot">${esc(who)}</span>: <span class="stream"></span>\n\n</div>`);
    log.scrollTop = log.scrollHeight;
    return log.lastElementChild.querySelector('.stream');
  }

  // Every message goes through the assistant router (search, reminders, maths, small talk).
  async function sendChat() {
    const text = $('#chat-in').value.trim();
    if (!text) return;
    $('#chat-in').value = '';
    appendChat('You', text);
    streamEl = startBubble('Assistant');
    try {
      const r = await call('ask', { text });
      streamEl.textContent = r.reply;
      renderReminders(r.reminders);
    } catch (err) {
      streamEl.textContent = 'Error: ' + err.message;
    }
    streamEl = null;
  }  $('#chat-send').onclick = sendChat;
  $('#chat-in').addEventListener('keydown', e => { if (e.key === 'Enter') sendChat(); });
  $('#clear-done').onclick = async () => renderReminders(await call('clearDone'));

  // ── browser (text mode) ────────────────────────────────────────────────
  async function browse() {
    const addr = $('#addr').value.trim();
    if (!addr) return;
    $('#page-text').textContent = 'Loading…';
    try {
      const r = await call('fetch', { url: addr });
      $('#addr').value = r.url;
      $('#page-text').textContent = r.text || '(no readable text)';
    } catch (err) { $('#page-text').textContent = 'Could not load page: ' + err.message; }
  }
  $('#addr-go').onclick = browse;
  $('#addr').addEventListener('keydown', e => { if (e.key === 'Enter') browse(); });
  $('#addr-ext').onclick = () => { const a = $('#addr').value.trim(); if (a) call('openExternal', { url: a }); };
  loaders.browser = async () => {
    const v = await call('version');
    $('#browser-engine').textContent = 'Page text is fetched and displayed by the app (scripts are not run).';
  };

  // ── security ───────────────────────────────────────────────────────────
  loaders.security = async () => {
    const [v, s] = await Promise.all([call('version'), call('stats')]);
    const rows = [
      ['Encryption', 'AES-256-CBC via Windows CNG', true],
      ['Key derivation', 'PBKDF2-SHA256, 200,000 iterations (notes)', true],
      ['Notes lock', s.unlocked ? 'Unlocked for this session' : 'Locked until you enter the password', !s.unlocked],
      ['Browser engine', v.browser ? 'WebView2 runtime detected' : 'WebView2 missing (classic UI in use)', v.browser],
      ['Network', 'Offline. Data only leaves the PC when you use the browser tool.', true]
    ];
    $('#security').innerHTML = rows.map(([t, d, ok]) =>
      `<div class="sec-row"><span class="dot ${ok ? 'ok' : 'warn'}"></span><div><b>${esc(t)}</b><small>${esc(d)}</small></div></div>`).join('');
  };

  // ── settings ───────────────────────────────────────────────────────────
  loaders.settings = async () => {
    const v = await call('version');
    $('#about').textContent = 'Core v' + v.core + ' · WebView2 ' + (v.browser ? 'available' : 'missing') + ' · ' + v.dataDir;
  };

  $('#set-pw').onclick = async () => {
    const v = await modal({ title: 'Change master password', okText: 'Change', fields: [
      { name: 'oldPassword', label: 'Current password', type: 'password' },
      { name: 'newPassword', label: 'New password (8+ characters)', type: 'password' },
      { name: 'confirm', label: 'Confirm new password', type: 'password' }] });
    if (!v) return;
    if (v.newPassword.length < 8) return toast('New password must be at least 8 characters.');
    if (v.newPassword !== v.confirm) return toast('Passwords do not match.');
    try { await call('changePassword', { oldPassword: v.oldPassword, newPassword: v.newPassword }); toast('Master password changed.'); }
    catch (err) { toast(err.message); }
  };
  $('#set-export').onclick = async () => { const p = await call('export'); if (p) toast('Exported to ' + p); };
  $('#set-import').onclick = async () => { const r = await call('import'); if (r) toast(`Imported ${r.imported}, skipped ${r.skipped}, errors ${r.errors}`); };
  $('#set-backup').onclick = async () => { const p = await call('backup'); toast('Backup saved: ' + p); };

  // ── global wiring ──────────────────────────────────────────────────────
  $$('.nav-btn').forEach(b => b.onclick = () => show(b.dataset.page));
  $$('[data-action="new"]').forEach(b => b.onclick = newEntry);
  $$('[data-go]').forEach(b => b.addEventListener('click', () => {
    if (b.dataset.go === 'vault' && b.dataset.filter) {
      filter = b.dataset.filter;
      $$('#chips .chip').forEach(x => x.classList.toggle('active', x.dataset.filter === filter));
    }
    show(b.dataset.go);
  }));

  document.addEventListener('keydown', (e) => {
    const ctrl = e.ctrlKey || e.metaKey;
    if (ctrl && e.key.toLowerCase() === 's' && current === 'vault') { e.preventDefault(); saveCurrent(); }
    else if (ctrl && e.key.toLowerCase() === 'n') { e.preventDefault(); newEntry(); }
    else if (ctrl && e.key.toLowerCase() === 'f') { e.preventDefault(); show('vault'); $('#vault-search').focus(); }
  });

  // Boot
  call('version').then(v => {
    $('#engine').innerHTML = 'Core v' + esc(v.core) + '<br>Encrypted · offline';
  }).catch(() => {});
  show('dashboard');
})();




