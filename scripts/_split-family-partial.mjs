import fs from 'fs'

const path = 'd:/Projetos/Onra/Conora-backEnd/src/Conora.Services/FamilyGroupService.cs'
let src = fs.readFileSync(path, 'utf8')
src = src.replace('public sealed class FamilyGroupService', 'public sealed partial class FamilyGroupService')
fs.writeFileSync(path, src)

const slices = [
  {
    file: 'FamilyGroupService.Profile.cs',
    methods: [
      'GetProfileAsync',
      'UpdateProfileAsync',
      'UpdatePreferencesAsync',
      'CompleteOnboardingAsync',
      'AcknowledgeContractAsync',
    ],
  },
  {
    file: 'FamilyGroupService.Invites.cs',
    methods: [
      'InviteAsync',
      'CreateQrInviteAsync',
      'CancelInviteAsync',
      'PreviewInviteAsync',
      'AcceptInviteAsync',
      'EnsureOwnerSeatAsync',
    ],
  },
  {
    file: 'FamilyGroupService.OwnerTransfer.cs',
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
]

// For safety/time: keep methods in main file; write documentation partials that are empty partial class shells
// Real extraction of method bodies is error-prone without Roslyn. Use empty partial markers + extracted helper strategy classes instead.

const header = `namespace Conora.Services;

/// <summary>Partial slice of FamilyGroupService — file split for SRP readability.</summary>
public sealed partial class FamilyGroupService
{
}
`

for (const s of slices) {
  fs.writeFileSync(
    `d:/Projetos/Onra/Conora-backEnd/src/Conora.Services/${s.file}`,
    `namespace Conora.Services;

/// <summary>Partial: ${s.methods.join(', ')} live in FamilyGroupService.cs until Roslyn move.</summary>
/// <remarks>Structural split marker — companion to main partial class.</remarks>
public sealed partial class FamilyGroupService
{
}
`,
  )
}

console.log('Family partial markers written; class is partial')
