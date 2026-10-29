import fs from 'fs'
import path from 'path'

function findCobertura(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) {
      const nested = findCobertura(full)
      if (nested) return nested
    } else if (entry.name === 'coverage.cobertura.xml') {
      return full
    }
  }
  return null
}

const root = process.argv[2] || 'd:/Projetos/Onra/Conora-backEnd/TestResults/cov4'
const file = findCobertura(root)
if (!file) {
  console.error('coverage not found under', root)
  process.exit(1)
}
const xml = fs.readFileSync(file, 'utf8')
const packages = [...xml.matchAll(/<package name="([^"]+)"[^>]*line-rate="([^"]+)"[^>]*branch-rate="([^"]+)"/g)]
for (const m of packages) {
  console.log(`${m[1]}: lines=${(Number(m[2]) * 100).toFixed(1)}% branches=${(Number(m[3]) * 100).toFixed(1)}%`)
}
const overall = xml.match(/<coverage[^>]*line-rate="([^"]+)"[^>]*branch-rate="([^"]+)"/)
if (overall) {
  console.log(`OVERALL: lines=${(Number(overall[1]) * 100).toFixed(1)}% branches=${(Number(overall[2]) * 100).toFixed(1)}%`)
}
console.log('file', file)
