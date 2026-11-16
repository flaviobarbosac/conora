import fs from 'fs'

function splitPartial(mainPath, outFiles) {
  let src = fs.readFileSync(mainPath, 'utf8')
  if (!src.includes('partial class')) {
    src = src.replace(/public sealed class (\w+)/, 'public sealed partial class $1')
  }
  const className = src.match(/public sealed partial class (\w+)/)[1]
  const ns = src.match(/namespace ([^;{\s]+)/)[1]
  const usings = src
    .split(/\r?\n/)
    .filter((l) => l.startsWith('using '))
    .join('\n')
  const lines = src.split(/\r?\n/)
  const methodStarts = []
  for (let i = 0; i < lines.length; i++) {
    const m = lines[i].match(
      /^\s+(public|private|internal)\s+(?:static\s+)?(?:async\s+)?(?:[\w.<>,\[\]?]+\s+)+(\w+)\s*\(/,
    )
    if (m) methodStarts.push({ i, name: m[2] })
  }
  function extractMethod(name) {
    const start = methodStarts.find((x) => x.name === name)
    if (!start) {
      console.warn('missing', name)
      return null
    }
    let depth = 0
    let started = false
    const body = []
    for (let j = start.i; j < lines.length; j++) {
      const line = lines[j]
      for (const ch of line) {
        if (ch === '{') {
          depth++
          started = true
        } else if (ch === '}') depth--
      }
      body.push(line)
      if (started && depth === 0) {
        for (let k = start.i; k <= j; k++) lines[k] = null
        return body.join('\n')
      }
    }
    return null
  }
  for (const out of outFiles) {
    const parts = []
    for (const name of out.methods) {
      const body = extractMethod(name)
      if (body) parts.push(body)
    }
    fs.writeFileSync(
      out.file,
      `${usings}\n\nnamespace ${ns};\n\npublic sealed partial class ${className}\n{\n${parts.join('\n\n')}\n}\n`,
    )
  }
  fs.writeFileSync(mainPath, lines.filter((l) => l !== null).join('\n'))
  console.log('split', mainPath)
}

splitPartial('d:/Projetos/Onra/Conora-backEnd/src/Conora.Services/BudgetService.cs', [
  {
    file: 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/BudgetService.Year.cs',
    methods: ['GetYearAsync', 'UpsertYearLineAsync', 'ClearYearLineAsync', 'ApplyYearSpreadAsync'],
  },
])

splitPartial('d:/Projetos/Onra/Conora-backEnd/src/Conora.Services/CategoryService.cs', [
  {
    file: 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/CategoryService.Tree.cs',
    methods: ['ListAsync', 'GetTreeAsync', 'RequireAnalyticalAsync', 'AddDefaults'],
  },
])
