// Run with Node and jsdom available on NODE_PATH.
const { JSDOM } = require('jsdom');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const assert = require('node:assert/strict');

const rows = Array.from({ length: 65 }, (_, i) => `<tr><td>Player ${i + 1}</td><td>${(i + 1) * 100}</td><td>${i % 2 ? 'Online' : 'Offline'}</td><td><form><button>Action</button></form></td></tr>`).join('');
const table = `<div class="table"><table><thead><tr><th>Name</th><th>Score</th><th>Status</th><th></th></tr></thead><tbody>${rows}</tbody></table></div>`;
const html = `<body data-refresh="10"><div class="admin-workspace"><div><span id="refresh-status"></span><button id="refresh-now">Refresh</button></div><section data-refresh-region="Players">${table.replaceAll('<form>', '').replaceAll('</form>', '')}</section></div></body>`;
const dom = new JSDOM(html, { runScripts: 'outside-only', url: 'http://localhost/admin', pretendToBeVisual: true });
const { window } = dom;
const document = window.document;
window.setInterval = () => 0;
window.fetch = async () => ({ ok: true, text: async () => html });
window.eval(readFileSync(resolve(__dirname, '../../../src/OpenScct.Master/Admin/assets/admin.js'), 'utf8'));
function visible() { return [...document.querySelectorAll('tbody tr')].filter(row => !row.hidden); }
function input(selector, value, event = 'input') {
  const node = document.querySelector(selector); node.value = value;
  node.dispatchEvent(new window.Event(event, { bubbles: true }));
}
function next() { document.querySelector('.table-pagination button:last-child').click(); }
async function run() {
  assert.equal(visible().length, 25);
  next(); assert.match(visible()[0].textContent, /^Player 26/);
  next(); assert.equal(visible().length, 15);
  assert.equal(document.querySelector('.table-pagination button:last-child').disabled, true);
  input('.table-controls input', 'Player 65'); assert.equal(visible().length, 1);
  assert.match(document.querySelector('.table-pagination span').textContent, /Page 1 of 1/);
  input('.table-controls input', '');
  input('.table-filters td:nth-child(3) input', 'online');
  assert.match(document.querySelector('.table-pagination span').textContent, /32 of 65/);
  document.querySelectorAll('.table-sort')[1].click();
  assert.equal(visible()[0].cells[1].textContent, '200');
  document.querySelectorAll('.table-sort')[1].click();
  assert.equal(visible()[0].cells[1].textContent, '6400');
  assert.equal(document.querySelectorAll('th')[1].getAttribute('aria-sort'), 'descending');
  input('.table-controls label:nth-child(3) select', '10', 'change');
  next(); assert.equal(visible().length, 10);
  const before = visible().map(row => row.textContent);
  document.querySelector('#refresh-now').click();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(visible().map(row => row.textContent), before);
  assert.equal(document.querySelectorAll('.table-controls').length, 1);
  assert.equal(document.querySelector('.table-filters td:nth-child(3) input').value, 'online');
  input('.table-controls input', 'no such player');
  assert.equal(visible().length, 1); assert.equal(visible()[0].textContent, 'No rows match these filters.');
  document.querySelector('.table-controls > button').click();
  assert.equal(visible().length, 25); assert.match(visible()[0].textContent, /^Player 1/);
  // Action controls must not pollute searchable row values.
  input('.table-controls input', 'Action');
  assert.equal(visible()[0].textContent, 'No rows match these filters.');
  console.log('Admin tables: pagination, filtering, sorting, reset and live refresh passed.');
  window.close();
}
run().catch(error => { console.error(error); window.close(); process.exitCode = 1; });
