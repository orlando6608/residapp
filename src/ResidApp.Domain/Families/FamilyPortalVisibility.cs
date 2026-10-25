using ResidApp.Domain.Enfermeria;

namespace ResidApp.Domain.Families;

/// <summary>
/// Lo que la familia ve en el Portal Familiar de un comunicado (FAM-07, portal-familiar.md historia 2): solo lo ya publicado y mientras siga en
/// su ventana. Una ordinaria se ve 30 días y una relevante 6 meses, contados desde que se publicó, no desde que se
/// preparó (confirmado por CJ, portal 1.2). Publicado es lo mismo que para el personal (FamilyCommunicationSchedule.IsPublished): llegó su hora o se publicó antes.
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

    /// <summary>Nueva para un familiar es una novedad publicada después de la última vez que abrió al residente; si nunca lo abrió, todas (CJ, tercera
    /// 6.3; lo de la primera visita es suposición mía).</summary>
    public static bool IsNewSince(DateTimeOffset publishedAt, DateTimeOffset? lastVisitAt) => lastVisitAt is not { } visit || publishedAt > visit;

    /// <summary>Una correctora sale en el momento de guardarla y deja de verse cuando deja de verse el comunicado que corrige (CJ, portal 4.2): sin
    /// el original no se entiende, y no alarga lo que la familia ve.</summary>
    public static bool IsCorrectionVisible(
        DateTimeOffset correctionPublishedAt, FamilyCommunicationType originalType, DateTimeOffset originalPublishedAt, DateTimeOffset now) =>
        now >= correctionPublishedAt && now < VisibleUntil(originalType, originalPublishedAt);
}
