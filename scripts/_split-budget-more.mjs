import fs from 'fs'

function splitPartial(mainPath, outFiles) {
  let src = fs.readFileSync(mainPath, 'utf8')
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
    const existing = fs.existsSync(out.file) ? fs.readFileSync(out.file, 'utf8') : null
    if (existing && out.append) {
      const injected = parts.join('\n\n')
      const next = existing.replace(/\}\s*$/, `${injected}\n}\n`)
      fs.writeFileSync(out.file, next)
    } else {
      fs.writeFileSync(
        out.file,
        `${usings}\n\nnamespace ${ns};\n\npublic sealed partial class ${className}\n{\n${parts.join('\n\n')}\n}\n`,
      )
    }
  }
  fs.writeFileSync(mainPath, lines.filter((l) => l !== null).join('\n'))
}

splitPartial('d:/Projetos/Onra/Conora-backEnd/src/Conora.Services/BudgetService.cs', [
  {
    file: 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/BudgetService.Mutations.cs',
    methods: [
      'UpsertAsync',
      'DeleteLineAsync',
      'CopyFromPreviousAsync',
      'RepeatAsync',
      'InstallmentAsync',
      'SyncPlannedTitheAsync',
      'UpsertPlannedForAccountAsync',
      'AddPayableFromBoletoAsync',
      'ClearPlannedForAccountAsync',
    ],
  },
])

splitPartial('d:/Projetos/Onra/Conora-backEnd/src/Conora.Services/CategoryService.cs', [
  {
    file: 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/CategoryService.Mutations.cs',
    methods: [
      'CreateAsync',
      'UpdateAsync',
      'SetActiveAsync',
      'DeleteAsync',
      'ApproveAsync',
      'RejectAsync',
      'UpdateSharingAsync',
    ],
  },
])

console.log('more splits done')
