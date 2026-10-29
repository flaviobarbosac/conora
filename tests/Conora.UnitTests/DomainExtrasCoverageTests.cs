using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Xunit;

namespace Conora.UnitTests;

public class DomainExtrasCoverageTests
{
    [Fact]
    public void ReceiptIntake_ocr_classify_and_fail_flow()
    {
        var intake = ReceiptIntake.Create(ReceiptChannel.App, "obj/key", TimeSpan.FromHours(1));
        Assert.Equal(ReceiptIntakeStatus.Processing, intake.Status);
        Assert.True(intake.AlignPurposeFromPaidAmount(false));
        Assert.Equal(ReceiptPurpose.BudgetBoleto, intake.Purpose);
        Assert.False(intake.AlignPurposeFromPaidAmount(false));
        Assert.True(intake.AlignPurposeFromPaidAmount(true));
        Assert.Equal(ReceiptPurpose.Expense, intake.Purpose);

        intake.ApplyOcr("123", "Loja", 10m, DateTime.UtcNow, null, null);
        Assert.Equal(ReceiptIntakeStatus.NeedsClassification, intake.Status);
        intake.SetClassification("ESS_RENT");
        Assert.Equal("ESS_RENT", intake.ProposedCategoryCode);
        intake.RetargetProposedCode("ESS_POWER");
        Assert.Equal("ESS_POWER", intake.ProposedCategoryCode);
        intake.RequireUserClassification();
        Assert.Equal(ReceiptIntakeStatus.NeedsClassification, intake.Status);
        intake.MarkFailed(new string('x', 600));
        Assert.Equal(ReceiptIntakeStatus.Failed, intake.Status);
        Assert.Equal(500, intake.FailureReason!.Length);
    }

    [Fact]
    public void ReceiptIntake_ready_when_proposed_code_present()
    {
        var intake = ReceiptIntake.Create(
            ReceiptChannel.WhatsApp,
            "wa/key",
            TimeSpan.FromMinutes(30),
            phoneE164: "+5511999999999");
        intake.ApplyOcr(null, "X", 5m, null, Guid.NewGuid(), "INC_SALARY");
        Assert.Equal(ReceiptIntakeStatus.Ready, intake.Status);
        Assert.Equal(WhatsAppReceiptAwaiting.Confirm, intake.WhatsAppAwaiting);
    }

    [Fact]
    public void FamilyInvite_email_and_qr_lifecycle()
    {
        var inviter = Guid.NewGuid();
        var invite = FamilyInvite.Create(inviter, " A@B.COM ", null, TimeSpan.FromDays(2));
        Assert.Equal("a@b.com", invite.Email);
        Assert.True(invite.IsOpen);
        Assert.False(invite.IsQrInvite);
        invite.BindGroup(Guid.NewGuid());
        invite.ReassignInviter(Guid.NewGuid());
        invite.Cancel();
        Assert.False(invite.IsOpen);

        var qr = FamilyInvite.CreateQr(inviter, Guid.NewGuid(), TimeSpan.FromHours(1));
        Assert.True(qr.IsQrInvite);
        qr.MarkAccepted(Guid.NewGuid());
        Assert.NotNull(qr.AcceptedAt);
        Assert.Throws<ValidationException>(() => FamilyInvite.Create(inviter, " ", null, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void LgpdRequest_export_ready_and_consume()
    {
        var export = LgpdRequest.Export();
        Assert.Equal("export", export.Kind);
        export.MarkExportReady("exports/a.xlsx", DateTime.UtcNow.AddDays(7), "hash");
        Assert.True(export.IsExportReadyOnStorage);
        export.ConsumeDownload();
        Assert.NotNull(export.DownloadConsumedAtUtc);
        export.ClearExportStorage();
        Assert.Null(export.ExportObjectKey);

        var deletion = LgpdRequest.Deletion();
        Assert.Throws<InvalidOperationException>(() =>
            deletion.MarkExportReady("x", DateTime.UtcNow.AddDays(1), "h"));
        deletion.Complete();
        Assert.NotNull(deletion.CompletedAtUtc);
    }

    [Fact]
    public void MilestoneCatalog_orders_and_display_age()
    {
        Assert.Equal(7, MilestoneCatalog.Ordered.Count);
        Assert.Equal("first-month-closed", MilestoneCatalog.ByKey("first-month-closed")!.Key);
        Assert.Equal(10, MilestoneCatalog.ByAge(10)!.Age);
        Assert.Equal(0, MilestoneCatalog.DisplayAge([]));
        Assert.Equal(8, MilestoneCatalog.DisplayAge([8]));
        Assert.Equal(8, MilestoneCatalog.DisplayAge([8, 12])); // gap stops at 8
        Assert.Equal(12, MilestoneCatalog.DisplayAge([8, 10, 12]));
    }

    [Fact]
    public void FamilyOwnerTransfer_authorize_accept_cancel()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var transfer = FamilyOwnerTransfer.Create(Guid.NewGuid(), from, to, TimeSpan.FromDays(1));
        Assert.True(transfer.IsOpen);
        transfer.Authorize(to);
        Assert.NotNull(transfer.AuthorizedAt);
        transfer.MarkBudgetsMoved(DateTime.UtcNow);
        transfer.Accept(to);
        Assert.NotNull(transfer.AcceptedAt);
        Assert.Throws<ValidationException>(() =>
            FamilyOwnerTransfer.Create(Guid.NewGuid(), from, from, TimeSpan.FromDays(1)));

        var open = FamilyOwnerTransfer.Create(Guid.NewGuid(), from, to, TimeSpan.FromDays(1));
        open.Cancel();
        Assert.NotNull(open.CancelledAt);
    }

    [Fact]
    public void ReceiptIntake_confirm_and_attach_supplier()
    {
        var intake = ReceiptIntake.Create(ReceiptChannel.App, "k", TimeSpan.FromHours(1));
        intake.ApplyOcr(null, "Loja", 9m, null, null, "ESS_WATER");
        intake.SetBankAccount(Guid.NewGuid());
        intake.LinkWhatsAppDraft(Guid.NewGuid());
        intake.Confirm();
        Assert.Equal(ReceiptIntakeStatus.Confirmed, intake.Status);
        intake.ClearImageKey();
        Assert.Equal(string.Empty, intake.ImageObjectKey);

        var other = ReceiptIntake.Create(ReceiptChannel.WhatsApp, "k2", TimeSpan.FromHours(1), "+5511");
        other.AttachSupplier(Guid.NewGuid(), "ESS_RENT");
        Assert.Equal(ReceiptIntakeStatus.Ready, other.Status);
        other.ReopenClassification();
        Assert.Equal(ReceiptIntakeStatus.NeedsClassification, other.Status);
    }

    [Fact]
    public void Domain_and_finance_exceptions_construct()
    {
        var id = Guid.NewGuid();
        _ = new UserNotFoundException(id);
        _ = new DuplicateEmailException("a@b.com");
        _ = new DuplicateCpfException();
        _ = new UniqueConstraintViolationException("ux_email");
        _ = new InvalidCredentialsException();
        _ = new InvalidTokenException();
        _ = new GoogleAuthDisabledException();
        _ = new AppleAuthDisabledException();
        _ = new NotFoundException("Conta", id);
        _ = new NotFoundException("msg");
        _ = new MonthClosedException("2026-10");
        _ = new PlanReadOnlyException();
        _ = new PlanReadOnlyException("custom");
        _ = new PlanBlockedException();
        _ = new ForbiddenException("nope");
        _ = new SystemCategoryProtectedException();
        _ = new ValidationException("field", "erro");
    }
}
