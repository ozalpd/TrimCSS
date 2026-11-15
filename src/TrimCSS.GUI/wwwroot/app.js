/**
 * TrimCSS Dashboard — app.js
 * Vanilla JS, zero external dependencies.
 * Communicates with the C# backend via InfiniFrame envelope web messaging:
 *   JS → C#:  window.infiniframe.messaging.sendMessageToHost('trimcss', jsonString)
 *   C# → JS:  window.infiniframe.messaging.assignMessageReceivedHandler('trimcss:response', fn)
 */

'use strict';

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------
const state = {
  folderPath:   null,
  scanResult:   null,
  activeFilter: 'all',   // 'all' | 'unused' | 'ambiguous'
  searchText:   '',
  fileFilter:   '',
  scanning:     false,
};

// ---------------------------------------------------------------------------
// DOM references (populated after DOMContentLoaded)
// ---------------------------------------------------------------------------
let dom = {};

function queryDom() {
  dom = {
    folderLabel:    document.getElementById('folder-path-label'),
    btnSelectFolder:document.getElementById('btn-select-folder'),
    btnScan:        document.getElementById('btn-scan'),
    scanLabel:      document.getElementById('scan-label'),
    scanSpinner:    document.getElementById('scan-spinner'),

    statTotal:      document.getElementById('stat-total'),
    statUnused:     document.getElementById('stat-unused'),
    statAmbiguous:  document.getElementById('stat-ambiguous'),
    statFiles:      document.getElementById('stat-files'),
    statTime:       document.getElementById('stat-time'),

    pills:          document.querySelectorAll('.pill'),
    searchInput:    document.getElementById('search-input'),
    fileFilter:     document.getElementById('file-filter'),
    resultCount:    document.getElementById('result-count'),

    emptyState:     document.getElementById('empty-state'),
    resultsTable:   document.getElementById('results-table'),
    tableBody:      document.getElementById('table-body'),
    selectAll:      document.getElementById('select-all'),
  };
}

// ---------------------------------------------------------------------------
// InfiniFrame messaging
// ---------------------------------------------------------------------------

const HOST_REQUEST_CHANNEL = 'trimcss';
const HOST_RESPONSE_CHANNEL = 'trimcss:response';

function getHostMessaging() {
  return (window.infiniframe && window.infiniframe.messaging) || null;
}

/**
 * Receive a message from C# and dispatch it.
 * Invoked by the InfiniFrame messaging layer for 'trimcss:response' envelopes.
 */
function handleHostMessage(rawJson) {
  let msg;
  try {
    msg = JSON.parse(rawJson);
  } catch (e) {
    console.error('[TrimCSS] Failed to parse message from C#:', rawJson, e);
    return;
  }
  dispatch(msg);
}

function registerHostMessageHandler() {
  const messaging = getHostMessaging();
  if (messaging && typeof messaging.assignMessageReceivedHandler === 'function') {
    messaging.assignMessageReceivedHandler(HOST_RESPONSE_CHANNEL, handleHostMessage);
    return true;
  }
  return false;
}

// The InfiniFrame bridge script is normally injected before app.js runs, but
// retry briefly in case the page loads before the host bridge is ready.
if (!registerHostMessageHandler()) {
  let attempts = 0;
  const timer = setInterval(() => {
    if (registerHostMessageHandler() || ++attempts >= 50) {
      clearInterval(timer);
    }
  }, 100);
}

/**
 * Send a message to C#.
 * @param {string} action
 * @param {object|null} payload
 */
function sendToCSharp(action, payload = null) {
  const json = JSON.stringify({ action, payload });
  const messaging = getHostMessaging();
  try {
    if (messaging && typeof messaging.sendMessageToHost === 'function') {
      messaging.sendMessageToHost(HOST_REQUEST_CHANNEL, json);
    } else {
      // Dev fallback: log to console when running outside InfiniFrame
      console.info('[TrimCSS] sendMessage (no host):', json);
    }
  } catch (e) {
    console.error('[TrimCSS] sendMessage failed:', e);
  }
}

/**
 * Dispatch a response received from C#.
 * @param {{ action: string, success: boolean, data: any, errorMessage: string|null }} response
 */
function dispatch(response) {
  if (!response || !response.action) return;

  if (!response.success) {
    console.error(`[TrimCSS] Action "${response.action}" failed:`, response.errorMessage);
    if (response.action === 'scanProject') {
      setScanLoading(false);
      showScanError(response.errorMessage);
    }
    return;
  }

  switch (response.action) {
    case 'selectFolder':
      onFolderSelected(response.data);
      break;
    case 'scanProject':
      onScanComplete(response.data);
      break;
    default:
      console.warn('[TrimCSS] Unknown action:', response.action);
  }
}

// ---------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------

function selectFolder() {
  sendToCSharp('selectFolder', null);
}

function scanProject() {
  if (!state.folderPath || state.scanning) return;
  setScanLoading(true);
  sendToCSharp('scanProject', { folderPath: state.folderPath });
}

// ---------------------------------------------------------------------------
// Response handlers
// ---------------------------------------------------------------------------

function onFolderSelected(data) {
  if (!data || !data.folderPath) return;
  state.folderPath = data.folderPath;
  dom.folderLabel.textContent = data.folderPath;
  dom.folderLabel.classList.add('has-path');
  dom.btnScan.disabled = false;

  // Reset scan results when a new folder is chosen
  state.scanResult = null;
  state.fileFilter = '';
  renderMetrics(null);
  renderAll();
}

function onScanComplete(data) {
  setScanLoading(false);
  if (!data) return;
  state.scanResult = data;
  renderMetrics(data);
  populateFileDropdown(data);
  renderAll();
}

// ---------------------------------------------------------------------------
// Loading / error states
// ---------------------------------------------------------------------------

function setScanLoading(loading) {
  state.scanning = loading;
  dom.btnScan.disabled = loading || !state.folderPath;
  dom.btnSelectFolder.disabled = loading;

  if (loading) {
    dom.scanLabel.classList.add('hidden');
    dom.scanSpinner.classList.remove('hidden');
  } else {
    dom.scanLabel.classList.remove('hidden');
    dom.scanSpinner.classList.add('hidden');
  }
}

function showScanError(message) {
  // Surface error in the empty state area
  dom.emptyState.classList.remove('hidden');
  dom.resultsTable.classList.add('hidden');
  dom.emptyState.innerHTML = `
    <div class="empty-state-icon">⚠️</div>
    <p style="color:var(--red)"><strong>Scan failed:</strong> ${escapeHtml(message || 'Unknown error')}</p>
  `;
}

// ---------------------------------------------------------------------------
// Render: Metrics Strip
// ---------------------------------------------------------------------------

/**
 * Format a TimeSpan string like "00:00:01.2345678" → "1.23s".
 * @param {string} ts
 * @returns {string}
 */
function formatTimeSpan(ts) {
  if (!ts) return '';
  // TimeSpan format: [-]d.hh:mm:ss.fffffff  or  hh:mm:ss.fffffff
  // Simplest: split on ':' and handle h/m/s
  try {
    const parts = ts.split(':');
    if (parts.length !== 3) return ts;
    const h = parseInt(parts[0], 10);
    const m = parseInt(parts[1], 10);
    const s = parseFloat(parts[2]);
    const totalMs = (h * 3600 + m * 60 + s) * 1000;
    if (totalMs < 1000) return `${totalMs.toFixed(0)}ms`;
    return `${(totalMs / 1000).toFixed(2)}s`;
  } catch {
    return ts;
  }
}

function renderMetrics(result) {
  if (!result) {
    dom.statTotal.textContent     = '—';
    dom.statUnused.textContent    = '—';
    dom.statAmbiguous.textContent = '—';
    dom.statFiles.textContent     = '—';
    dom.statTime.textContent      = '';
    return;
  }
  dom.statTotal.textContent     = result.totalCssSelectors ?? 0;
  dom.statUnused.textContent    = (result.unusedSelectors    ?? []).length;
  dom.statAmbiguous.textContent = (result.ambiguousSelectors ?? []).length;
  dom.statFiles.textContent     = result.scannedFilesCount ?? 0;
  dom.statTime.textContent      = formatTimeSpan(result.executionTime);
}

// ---------------------------------------------------------------------------
// Render: File Dropdown
// ---------------------------------------------------------------------------

function populateFileDropdown(result) {
  const all = [
    ...(result.unusedSelectors    ?? []),
    ...(result.ambiguousSelectors ?? []),
  ];
  const paths = [...new Set(all.map(s => s.filePath).filter(Boolean))].sort();

  // Rebuild options
  dom.fileFilter.innerHTML = '<option value="">All CSS Files</option>';
  for (const p of paths) {
    const opt = document.createElement('option');
    opt.value = p;
    opt.textContent = relativePath(p);
    dom.fileFilter.appendChild(opt);
  }
  state.fileFilter = '';
  dom.fileFilter.value = '';
}

// ---------------------------------------------------------------------------
// Render: Table
// ---------------------------------------------------------------------------

/**
 * Strip the project folder prefix from an absolute file path.
 * @param {string} filePath
 * @returns {string}
 */
function relativePath(filePath) {
  if (!filePath || !state.folderPath) return filePath || '';
  // Normalize separators
  const base = state.folderPath.replace(/\\/g, '/').replace(/\/$/, '');
  const fp   = filePath.replace(/\\/g, '/');
  if (fp.toLowerCase().startsWith(base.toLowerCase())) {
    return fp.slice(base.length).replace(/^\//, '');
  }
  return fp;
}

function escapeHtml(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/**
 * Build all filtered + searched rows and render them.
 */
function renderAll() {
  if (!state.scanResult) {
    dom.emptyState.classList.remove('hidden');
    dom.emptyState.innerHTML = `
      <div class="empty-state-icon">🔍</div>
      <p>Select a project folder and click <strong>Scan Project</strong> to detect unused CSS selectors.</p>
    `;
    dom.resultsTable.classList.add('hidden');
    if (dom.resultCount) dom.resultCount.textContent = '';
    return;
  }

  const { unusedSelectors = [], ambiguousSelectors = [] } = state.scanResult;

  // 1. Filter by active tab
  let rows = [];
  if (state.activeFilter === 'all') {
    rows = [
      ...unusedSelectors.map(s    => ({ ...s, _type: 'unused' })),
      ...ambiguousSelectors.map(s => ({ ...s, _type: 'ambiguous' })),
    ];
  } else if (state.activeFilter === 'unused') {
    rows = unusedSelectors.map(s => ({ ...s, _type: 'unused' }));
  } else {
    rows = ambiguousSelectors.map(s => ({ ...s, _type: 'ambiguous' }));
  }

  // 2. Filter by CSS file
  if (state.fileFilter) {
    rows = rows.filter(s => s.filePath === state.fileFilter);
  }

  // 3. Filter by search text
  const q = state.searchText.trim().toLowerCase();
  if (q) {
    rows = rows.filter(s =>
      (s.selectorText || '').toLowerCase().includes(q) ||
      (s.filePath     || '').toLowerCase().includes(q) ||
      (s.targetClasses || []).some(c => c.toLowerCase().includes(q))
    );
  }

  // Update result count
  if (dom.resultCount) {
    dom.resultCount.textContent = rows.length === 1
      ? '1 result'
      : `${rows.length} results`;
  }

  if (rows.length === 0) {
    dom.emptyState.classList.remove('hidden');
    dom.emptyState.classList.add('no-results');
    dom.emptyState.innerHTML = `
      <div class="empty-state-icon">✅</div>
      <p>${q || state.fileFilter
        ? 'No results match your filter.'
        : 'No unused or ambiguous selectors found — your CSS looks clean!'
      }</p>
    `;
    dom.resultsTable.classList.add('hidden');
    return;
  }

  dom.emptyState.classList.add('hidden');
  dom.emptyState.classList.remove('no-results');
  dom.resultsTable.classList.remove('hidden');

  // Build table rows
  const fragment = document.createDocumentFragment();
  for (const selector of rows) {
    const tr = document.createElement('tr');

    // Checkbox
    const tdCheck = document.createElement('td');
    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.addEventListener('change', () => {
      tr.classList.toggle('selected', cb.checked);
      syncSelectAll();
    });
    tdCheck.appendChild(cb);
    tr.appendChild(tdCheck);

    // Status Badge
    const tdStatus = document.createElement('td');
    const badge = document.createElement('span');
    badge.className = selector._type === 'unused' ? 'badge badge-unused' : 'badge badge-ambiguous';
    badge.textContent = selector._type === 'unused' ? 'Unused' : 'Ambiguous';
    tdStatus.appendChild(badge);
    tr.appendChild(tdStatus);

    // Selector text
    const tdSelector = document.createElement('td');
    const selectorSpan = document.createElement('span');
    selectorSpan.className = 'selector-text';
    selectorSpan.textContent = selector.selectorText || '';
    tdSelector.appendChild(selectorSpan);
    tr.appendChild(tdSelector);

    // Target classes
    const tdClasses = document.createElement('td');
    const classWrap = document.createElement('div');
    classWrap.className = 'target-classes';
    for (const cls of (selector.targetClasses || [])) {
      const chip = document.createElement('span');
      chip.className = 'class-chip';
      chip.textContent = cls;
      classWrap.appendChild(chip);
    }
    tdClasses.appendChild(classWrap);
    tr.appendChild(tdClasses);

    // File path
    const tdFile = document.createElement('td');
    const fileSpan = document.createElement('span');
    fileSpan.className = 'file-path';
    fileSpan.textContent = relativePath(selector.filePath);
    fileSpan.title = selector.filePath || '';
    tdFile.appendChild(fileSpan);
    tr.appendChild(tdFile);

    // Location
    const tdLoc = document.createElement('td');
    const locSpan = document.createElement('span');
    locSpan.className = 'location';
    locSpan.textContent = `${selector.lineNumber ?? 0}:${selector.columnNumber ?? 0}`;
    tdLoc.appendChild(locSpan);
    tr.appendChild(tdLoc);

    fragment.appendChild(tr);
  }

  dom.tableBody.innerHTML = '';
  dom.tableBody.appendChild(fragment);

  // Reset select-all checkbox
  if (dom.selectAll) dom.selectAll.checked = false;
}

// ---------------------------------------------------------------------------
// Select-all logic
// ---------------------------------------------------------------------------

function syncSelectAll() {
  const checkboxes = dom.tableBody.querySelectorAll('input[type="checkbox"]');
  const allChecked  = checkboxes.length > 0 && [...checkboxes].every(c => c.checked);
  dom.selectAll.checked = allChecked;
  dom.selectAll.indeterminate = !allChecked && [...checkboxes].some(c => c.checked);
}

// ---------------------------------------------------------------------------
// Event wiring
// ---------------------------------------------------------------------------

function wireEvents() {
  dom.btnSelectFolder.addEventListener('click', selectFolder);
  dom.btnScan.addEventListener('click', scanProject);

  // Pill tabs
  dom.pills.forEach(pill => {
    pill.addEventListener('click', () => {
      dom.pills.forEach(p => p.classList.remove('active'));
      pill.classList.add('active');
      state.activeFilter = pill.dataset.filter;
      renderAll();
    });
  });

  // Search
  dom.searchInput.addEventListener('input', () => {
    state.searchText = dom.searchInput.value;
    renderAll();
  });

  // File filter dropdown
  dom.fileFilter.addEventListener('change', () => {
    state.fileFilter = dom.fileFilter.value;
    renderAll();
  });

  // Select-all checkbox
  dom.selectAll.addEventListener('change', () => {
    const checkboxes = dom.tableBody.querySelectorAll('input[type="checkbox"]');
    checkboxes.forEach(cb => {
      cb.checked = dom.selectAll.checked;
      cb.closest('tr').classList.toggle('selected', dom.selectAll.checked);
    });
  });
}

// ---------------------------------------------------------------------------
// Bootstrap
// ---------------------------------------------------------------------------

document.addEventListener('DOMContentLoaded', () => {
  queryDom();
  wireEvents();
  renderMetrics(null);
  renderAll();

  console.info('[TrimCSS] Dashboard ready.');
});
