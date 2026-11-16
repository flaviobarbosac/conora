import fs from 'fs'

function splitPartial(mainPath, outFiles) {
  let src = fs.readFileSync(mainPath, 'utf8')
  if (!src.includes('partial class')) {
    src = src.replace(/public sealed class (\w+)/, 'public sealed partial class $1')
  }
  const classMatch = src.match(/public sealed partial class (\w+)/)
  const className = classMatch[1]
  const nsMatch = src.match(/namespace ([^;{\s]+)/)
  const ns = nsMatch[1]

  // usings block
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
    if (m) methodStarts.push({ i, name: m[2], access: m[1] })
  }

  function extractMethod(name) {
    const start = methodStarts.find((x) => x.name === name)
    if (!start) return null
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
        // clear from lines
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
      else console.warn('missing', name, 'in', mainPath)
    }
    const content = `${usings}

namespace ${ns};

public sealed partial class ${className}
{
${parts.join('\n\n')}
}
`
    fs.writeFileSync(out.file, content)
  }

  // rewrite main without extracted lines
  const remaining = lines.filter((l) => l !== null).join('\n')
  fs.writeFileSync(mainPath, remaining)
  console.log('split', mainPath, '->', outFiles.map((f) => f.file).join(', '))
}

splitPartial('d:/Projetos/Onra/Conora-backEnd/src/Conora.Services/FamilyGroupService.cs', [
  {
    file: 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/FamilyGroupService.Profile.cs',
    methods: [
      'GetProfileAsync',
      'UpdateProfileAsync',
      'UpdatePreferencesAsync',
      'CompleteOnboardingAsync',
      'AcknowledgeContractAsync',
      'RequireUserAsync',
    ],
  },
  {
    file: 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/FamilyGroupService.Invites.cs',
    methods: [
      'InviteAsync',
      'CreateQrInviteAsync',
      'CancelInviteAsync',
      'PreviewInviteAsync',
      'AcceptInviteAsync',
      'EnsureOwnerSeatAsync',
      'LeaveAsync',
      'RemoveMemberAsync',
      'RemoveMemberCoreAsync',
      'MarkUserDataAsFamilyAsync',
      'MarkUserDataAsPersonalAsync',
      'MarkNoticeReadAsync',
    ],
  },
  {
    file: 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/FamilyGroupService.OwnerTransfer.cs',
    methods: [
      'RequestOwnerTransferAsync',
      'CancelOwnerTransferAsync',
      'AcceptOwnerTransferAsync',
      'CompleteOwnerTransferAsync',
      'ConfirmStoreCancelledAsync',
      'MoveBudgetsOnStoreCancellationAsync',
      'ReplaceBudgetsFromHandoverAsync',
    ],
  },
])

// BudgetService + CategoryService: mark partial and extract large private helpers into *Rules files as nested static later
for (const p of [
  'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/BudgetService.cs',
  'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/CategoryService.cs',
]) {
  let s = fs.readFileSync(p, 'utf8')
  if (!s.includes('partial class')) {
    s = s.replace('public sealed class', 'public sealed partial class')
    fs.writeFileSync(p, s)
    console.log('partial', p)
  }
}
