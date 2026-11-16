import fs from 'fs'

const mainPath = 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/BudgetService.cs'
const queriesPath = 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/BudgetService.Queries.cs'
let src = fs.readFileSync(mainPath, 'utf8')
const methodNames = ['GetActualsAsync', 'GetAsync', 'GetCategoryBreakdownAsync']

function extractMethod(source, name) {
  const sig = new RegExp(`public async Task[\\s\\S]*?\\b${name}\\s*\\(`)
  const m = sig.exec(source)
  if (!m) return null
  const start = m.index
  let i = source.indexOf('{', start)
  let depth = 0
  for (; i < source.length; i++) {
    if (source[i] === '{') depth++
    else if (source[i] === '}') {
      depth--
      if (depth === 0) {
        i++
        break
      }
    }
  }
  return { start, end: i, text: source.slice(start, i) }
}

const extracted = []
for (const name of methodNames) {
  const hit = extractMethod(src, name)
  if (!hit) {
    console.log('miss', name)
    continue
  }
  extracted.push(hit.text)
  src = src.slice(0, hit.start) + src.slice(hit.end)
  console.log('moved', name, hit.text.split('\n').length)
}

src = src.replace(/\n{3,}/g, '\n\n')
fs.writeFileSync(mainPath, src)

const header = `using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class BudgetService
{
`

fs.writeFileSync(queriesPath, `${header}${extracted.join('\n\n')}\n}\n`)
console.log('main lines', src.split('\n').length, 'queries lines', extracted.join('\n').split('\n').length)
