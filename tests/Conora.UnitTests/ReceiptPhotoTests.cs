using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class ReceiptPhotoTests
{
    private static readonly DateTime When = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Photo_without_cnpj_supplier_date_or_amount_lists_what_is_missing()
    {
        var intake = ReceiptIntake.Create(ReceiptChannel.App, "foto", TimeSpan.FromHours(1));

        Assert.Equal(["cnpj", "fornecedor", "data", "valor"], ReceiptIntakeService.MissingPhotoFields(intake));
        Assert.False(intake.HasRequiredPhotoFields());

        intake.ApplyOcr("00.000.000/0000-00", null, null, null, null, null);
        Assert.Equal(["fornecedor", "data", "valor"], ReceiptIntakeService.MissingPhotoFields(intake));

        intake.ApplyOcr("00.000.000/0000-00", "Mercado", 0, When, null, null);
        Assert.Contains("valor", ReceiptIntakeService.MissingPhotoFields(intake));
    }

    [Fact]
    public void Complete_photo_asks_nothing_and_keeps_account_and_category_out_of_the_required_list()
    {
        var intake = ReceiptIntake.Create(ReceiptChannel.App, "foto", TimeSpan.FromHours(1));
        intake.ApplyOcr("00.000.000/0000-00", "Mercado", 12.50m, When, null, "ESS_RENT");

        Assert.True(intake.HasRequiredPhotoFields());
        Assert.Empty(ReceiptIntakeService.MissingPhotoFields(intake));
    }

    [Fact]
    public void Manual_yes_keeps_the_photo_and_manual_no_uses_the_commercial_message()
    {
        var app = ReceiptIntake.Create(ReceiptChannel.App, "foto", TimeSpan.FromHours(1));
        app.AskManualFill();
        app.AcceptManualFill();
        Assert.Equal(WhatsAppReceiptAwaiting.None, app.WhatsAppAwaiting);

        var whatsApp = ReceiptIntake.Create(ReceiptChannel.WhatsApp, "foto", TimeSpan.FromHours(1), "5527999999999");
        whatsApp.AskManualFill();
        Assert.Equal(WhatsAppReceiptAwaiting.ManualChoice, whatsApp.WhatsAppAwaiting);
        whatsApp.AcceptManualFill();
        Assert.Equal(WhatsAppReceiptAwaiting.Classification, whatsApp.WhatsAppAwaiting);

        var ready = ReceiptIntake.Create(ReceiptChannel.WhatsApp, "foto", TimeSpan.FromHours(1), "5527999999999");
        ready.ApplyOcr("00.000.000/0000-00", "Mercado", 10m, When, null, "ESS_RENT");
        ready.AskManualFill();
        ready.AcceptManualFill();
        Assert.Equal(WhatsAppReceiptAwaiting.Confirm, ready.WhatsAppAwaiting);

        var declined = ReceiptIntake.Create(ReceiptChannel.App, "foto", TimeSpan.FromHours(1));
        declined.MarkFailed(ReceiptPhotoCopy.ClearerPhoto);
        Assert.Equal(ReceiptIntakeStatus.Failed, declined.Status);
        Assert.Equal(ReceiptPhotoCopy.ClearerPhoto, declined.FailureReason);
        Assert.Contains("foto mais nítida", ReceiptPhotoCopy.ClearerPhoto);
        Assert.Contains("CNPJ", ReceiptPhotoCopy.ClearerPhoto);
        Assert.Equal("Não consegui ler tudo nesta foto. Quer preencher o que faltou?", ReceiptPhotoCopy.ManualQuestion);
    }
}
