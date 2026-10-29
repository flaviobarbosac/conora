import fs from 'fs'
import path from 'path'

function findCobertura(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) {
      const nested = findCobertura(full)
      if (nested) return nested
    } else if (entry.name === 'coverage.cobertura.xml') return full
  }
  return null
}

const file = findCobertura(process.argv[2])
const xml = fs.readFileSync(file, 'utf8')
const chunks = xml.split('<class ').slice(1)
const rows = []
for (const chunk of chunks) {
  const head = chunk.slice(0, chunk.indexOf('>'))
  const name = head.match(/name="([^"]+)"/)?.[1]
  const filename = head.match(/filename="([^"]+)"/)?.[1]
  const rate = Number(head.match(/line-rate="([^"]+)"/)?.[1] ?? 0)
  if (!filename?.includes('Conora.Domain')) continue
  const body = chunk.slice(0, chunk.indexOf('</class>'))
  const lines = [...body.matchAll(/<line [^>]*hits="(\d+)"/g)]
  const valid = lines.length
  const covered = lines.filter((m) => Number(m[1]) > 0).length
  rows.push({
    name,
    file: filename.replace(/\\/g, '/').split('/').slice(-2).join('/'),
    rate: rate * 100,
    covered,
    valid,
  })
}
rows.sort((a, b) => a.rate - b.rate || b.valid - a.valid)
for (const r of rows.slice(0, 40)) {
  console.log(
    `${r.rate.toFixed(1).padStart(5)}% ${String(r.covered + '/' + r.valid).padStart(9)} ${r.file} :: ${r.name.split('.').pop()}`,
  )
}
const cov = rows.reduce((s, r) => s + r.covered, 0)
const val = rows.reduce((s, r) => s + r.valid, 0)
console.log(`Domain ${((cov / val) * 100).toFixed(1)}% (${cov}/${val}) classes=${rows.length}`)
