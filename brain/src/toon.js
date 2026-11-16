/**
 * Segundo Cérebro — minimal TOON (Token-Oriented Object Notation) encoder.
 *
 * Why: when the Bibliotecário injects a list/catalog (e.g. several artifacts or
 * learnings) into the model context, JSON repeats every key on every row, which
 * is pure token waste. TOON lifts the shared keys into a single header and emits
 * the values once per row — >50% fewer tokens on uniform arrays. JSON stays the
 * storage/transport format; TOON is applied only right before injection.
 *
 * Use TOON only for uniform arrays of objects. For a small/irregular object the
 * gain is marginal, so encodeToon() falls back to compact JSON.
 *
 * Tabular form:
 *   rows[2]{id,title}:
 *     1,Regra A
 *     2,"Título, com vírgula"
 */

function needsQuote(s) {
  return /[",\n]/.test(s);
}

function cell(value) {
  if (value === null || value === undefined) return '';
  let s = typeof value === 'string' ? value : JSON.stringify(value);
  // Collapse newlines so each record stays on a single line.
  s = s.replace(/\r?\n/g, ' ');
  if (needsQuote(s)) s = '"' + s.replace(/"/g, '""') + '"';
  return s;
}

function isUniformObjectArray(arr) {
  if (!Array.isArray(arr) || arr.length === 0) return false;
  if (!arr.every((r) => r && typeof r === 'object' && !Array.isArray(r))) return false;
  const keys = Object.keys(arr[0]).join('|');
  return arr.every((r) => Object.keys(r).join('|') === keys);
}

/**
 * Encode data as TOON when it is a uniform array of objects; otherwise return
 * compact JSON. `name` labels the block (default "rows").
 */
function encodeToon(data, name = 'rows') {
  if (!isUniformObjectArray(data)) return JSON.stringify(data);
  const cols = Object.keys(data[0]);
  const header = `${name}[${data.length}]{${cols.join(',')}}:`;
  const lines = data.map((row) => '  ' + cols.map((c) => cell(row[c])).join(','));
  return [header, ...lines].join('\n');
}

module.exports = { encodeToon };
