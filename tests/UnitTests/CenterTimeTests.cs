using System.Text.RegularExpressions;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>
/// La hora del centro (zonas horarias por centro, F0): por defecto la de la península; la zona se fija por petición y se restaura; el «hoy» y la conversión salen de la zona
/// y no de la del servidor; y ningún código de la aplicación pregunta la hora local por su cuenta (prueba de arquitectura).
/// </summary>
public class CenterTimeTests
{
    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    [Fact]
    public void Sin_fijar_nada_la_zona_es_la_de_la_peninsula()
    {
        Assert.Equal("Europe/Madrid", CenterTime.Zone.Id);
        Assert.Same(CenterTime.Madrid, CenterTime.Zone);
    }

    [Fact]
    public void Use_fija_la_zona_hasta_que_se_libera_y_restaura_la_anterior()
    {
        var canarias = Zone("Atlantic/Canary");
        using (CenterTime.Use(canarias))
        {
            Assert.Same(canarias, CenterTime.Zone);
            using (CenterTime.Use(CenterTime.Madrid))
            {
                Assert.Same(CenterTime.Madrid, CenterTime.Zone);
            }

            Assert.Same(canarias, CenterTime.Zone);
        }

        Assert.Same(CenterTime.Madrid, CenterTime.Zone);
    }

    [Fact]
    public async Task La_zona_es_de_cada_peticion_y_sobrevive_a_los_await()
    {
        var canarias = Zone("Atlantic/Canary");
        async Task<string> InAsync(TimeZoneInfo zone)
        {
            using (CenterTime.Use(zone))
            {
                await Task.Delay(20);
                await Task.Yield();
                return CenterTime.Zone.Id;
            }
        }

        var results = await Task.WhenAll(InAsync(canarias), InAsync(CenterTime.Madrid), InAsync(canarias), InAsync(CenterTime.Madrid));

        Assert.Equal(["Atlantic/Canary", "Europe/Madrid", "Atlantic/Canary", "Europe/Madrid"], results);
        Assert.Same(CenterTime.Madrid, CenterTime.Zone);
    }

    [Theory]
    [InlineData("Europe/Madrid", "2026-01-15T23:30:00Z", "2026-01-16")]
    [InlineData("Atlantic/Canary", "2026-01-15T23:30:00Z", "2026-01-15")]
    [InlineData("Europe/Madrid", "2026-07-15T22:30:00Z", "2026-07-16")]
    [InlineData("Atlantic/Canary", "2026-07-15T22:30:00Z", "2026-07-15")]
    [InlineData("Atlantic/Canary", "2026-07-15T23:00:00Z", "2026-07-16")]
    public void El_hoy_es_el_del_centro_y_no_el_del_servidor(string zoneId, string instant, string expectedDay)
    {
        using var _ = CenterTime.Use(Zone(zoneId));

        Assert.Equal(DateOnly.Parse(expectedDay), CenterTime.TodayAt(DateTimeOffset.Parse(instant)));
    }

    [Theory]
    [InlineData("Europe/Madrid", "2026-01-15T12:00:00Z", "13:00")]
    [InlineData("Europe/Madrid", "2026-07-15T12:00:00Z", "14:00")]
    [InlineData("Atlantic/Canary", "2026-01-15T12:00:00Z", "12:00")]
    [InlineData("Atlantic/Canary", "2026-07-15T12:00:00Z", "13:00")]
    public void Local_convierte_un_instante_a_la_hora_del_centro_en_invierno_y_en_verano(string zoneId, string instant, string expected)
    {
        using var _ = CenterTime.Use(Zone(zoneId));

        Assert.Equal(expected, CenterTime.Local(DateTimeOffset.Parse(instant)).ToString("HH:mm"));
        // Una fecha y hora guardada en UTC (la base de datos la devuelve sin «tipo») se trata como UTC.
        Assert.Equal(expected, CenterTime.Local(DateTime.SpecifyKind(DateTime.Parse(instant.TrimEnd('Z')), DateTimeKind.Unspecified)).ToString("HH:mm"));
    }

    [Fact]
    public void FromLocal_entiende_la_hora_de_un_campo_datetime_local_con_la_zona_del_centro()
    {
        var local = new DateTime(2026, 7, 15, 10, 0, 0);

        using (CenterTime.Use(CenterTime.Madrid))
        {
            Assert.Equal(DateTimeOffset.Parse("2026-07-15T08:00:00Z"), CenterTime.FromLocal(local));
        }

        using (CenterTime.Use(Zone("Atlantic/Canary")))
        {
            Assert.Equal(DateTimeOffset.Parse("2026-07-15T09:00:00Z"), CenterTime.FromLocal(local));
        }
    }

    [Fact]
    public void FromLocal_con_una_hora_que_no_existe_por_el_cambio_de_hora_no_falla()
    {
        // En Madrid, el 29/03/2026 a las 02:00 los relojes saltan a las 03:00: las 02:30 no existen.
        using var _ = CenterTime.Use(CenterTime.Madrid);

        var instant = CenterTime.FromLocal(new DateTime(2026, 3, 29, 2, 30, 0));

        Assert.Equal(TimeSpan.FromHours(1), instant.Offset);
    }

    private static readonly string[] Forbidden =
    [
        @"TimeZoneInfo\.Local\b", @"\.ToLocalTime\(", @"\bDateTime\.Today\b", @"\bDateTime\.Now\b", @"\bDateTimeOffset\.Now\b", @"DateTimeKind\.Local\b",
    ];

    private static readonly string[] Allowed = ["CenterTime.cs", "ServerTimeZone.cs"];

    private static string Raiz()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Shared", "CenterTime.cs")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No se encuentra la raíz del repositorio.");
    }

    [Fact]
    public void Ningun_codigo_de_la_aplicacion_pregunta_la_hora_local_por_su_cuenta()
    {
        // La hora y el «hoy» salen de CenterTime: con TimeZoneInfo.Local, ToLocalTime(), DateTime.Today o DateTime.Now, la hora dependería de la zona del servidor y no de la del centro.
        var src = Path.Combine(Raiz(), "src");
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs") || f.EndsWith(".cshtml")))
        {
            var relative = Path.GetRelativePath(src, file);
            if (Allowed.Contains(Path.GetFileName(file)) || relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            {
                continue;
            }

            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("@*"))
                {
                    continue;
                }

                var code = Regex.Replace(line, @"(?<!:)//.*$", string.Empty);
                found.AddRange(Forbidden.Where(pattern => Regex.IsMatch(code, pattern)).Select(_ => $"{relative}:{number}: {trimmed.Trim()}"));
            }
        }

        Assert.True(found.Count == 0, "Usa CenterTime (ResidApp.Shared) en lugar de la hora local del servidor:" + Environment.NewLine + string.Join(Environment.NewLine, found));
    }
}
