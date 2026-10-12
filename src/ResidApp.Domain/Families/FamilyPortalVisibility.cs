using ResidApp.Domain.Enfermeria;

namespace ResidApp.Domain.Families;

/// <summary>
/// Lo que la familia ve en el Portal Familiar de un comunicado (FAM-07, portal-familiar.md historia 2): solo lo ya publicado y mientras siga en
/// su ventana. Una ordinaria se ve 30 días y una relevante 6 meses, contados desde que se publicó (suposición a confirmar con CJ: no desde que
/// se preparó). Publicado es lo mismo que para el personal (FamilyCommunicationSchedule.IsPublished): llegó su hora o se publicó antes.
/// </summary>
public static class FamilyPortalVisibility
{
    public const int OrdinaryDays = 30;
    public const int RelevantMonths = 6;

    /// <summary>Cuándo se publicó de verdad: antes de su hora si Administración o Dirección Clínica la adelantaron; si no, a su hora.</summary>
    public static DateTimeOffset PublishedAt(DateTimeOffset scheduledAt, DateTimeOffset? publishedEarlyAt) => publishedEarlyAt ?? scheduledAt;

    /// <summary>El primer momento a partir del cual un comunicado de ese tipo ya no se ve.</summary>
    public static DateTimeOffset VisibleUntil(FamilyCommunicationType type, DateTimeOffset publishedAt) => type == FamilyCommunicationType.Relevante
        ? publishedAt.AddMonths(RelevantMonths)
        : publishedAt.AddDays(OrdinaryDays);

    public static bool IsVisible(FamilyCommunicationType type, DateTimeOffset scheduledAt, DateTimeOffset? publishedEarlyAt, DateTimeOffset now) =>
        FamilyCommunicationSchedule.IsPublished(scheduledAt, publishedEarlyAt, now)
        && now < VisibleUntil(type, PublishedAt(scheduledAt, publishedEarlyAt));

    /// <summary>Una publicación que sale en el momento de guardarla (una correctora): se ve desde entonces y durante la ventana de su tipo.</summary>
    public static bool IsVisible(FamilyCommunicationType type, DateTimeOffset publishedAt, DateTimeOffset now) =>
        now >= publishedAt && now < VisibleUntil(type, publishedAt);
}
