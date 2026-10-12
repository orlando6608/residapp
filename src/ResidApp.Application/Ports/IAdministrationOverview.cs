namespace ResidApp.Application.Ports;

/// <summary>Los días que cuentan «caduca pronto» y «próximas» en el inicio de Administración (ADM-01; valores míos, sin confirmar con CJ).</summary>
public static class AdministrationOverviewRules
{
    public const int ExpiringSoonDays = 30;

    public const int ComingAppointmentDays = 7;
}

/// <summary>El momento en que se lee el inicio: el día natural del centro (Today), sus límites UTC y el fin de los próximos días. Hoy es [TodayStart,
/// TomorrowStart); «próximas» es [TomorrowStart, ComingEnd).</summary>
public sealed record AdministrationOverviewPeriod(
    DateOnly Today, DateTimeOffset Now, DateTimeOffset TodayStart, DateTimeOffset TomorrowStart, DateTimeOffset ComingEnd);

/// <summary>ADM-01: lo que Administración tiene que atender, solo con cifras (nada de nombres ni de contenido clínico). Los residentes sin habitación y
/// las autorizaciones son de los residentes de su ámbito; las solicitudes y las citas, de todo el centro, como su bandeja y su agenda.</summary>
public sealed record AdministrationOverview(
    int ResidentsWithoutRoom, int AuthorizationsPending, int AuthorizationsExpiringSoon, int AuthorizationsExpired,
    int AppointmentRequestsPending, int AppointmentRequestsAwaitingFamily, int AppointmentsToday, int AppointmentsComing)
{
    public int Authorizations => AuthorizationsPending + AuthorizationsExpiringSoon + AuthorizationsExpired;

    public int AppointmentRequests => AppointmentRequestsPending + AppointmentRequestsAwaitingFamily;

    /// <summary>Si no hay nada que atender (las citas de hoy y las próximas informan, no piden nada).</summary>
    public bool NothingPending => ResidentsWithoutRoom + Authorizations + AppointmentRequests == 0;
}

/// <summary>Lectura de las cifras del inicio de Administración. Comprueba en la propia consulta que quien mira es Administración del centro.</summary>
public interface IAdministrationOverviewReader
{
    Task<AdministrationOverview> ReadAsync(AccountAdministrationAccess access, AdministrationOverviewPeriod period, CancellationToken ct = default);
}
