using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.IA;
using ResidApp.Infrastructure.Identity;
using ResidApp.Infrastructure.Intercambio;
using ResidApp.Infrastructure.Pdf;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<InvalidIdentifierFilter>();
    // Mensajes de enlace del modelo en español (p. ej., un identificador mal escrito); por defecto salen en inglés.
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"«{value}» no es un valor válido para {field}.");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"«{value}» no es un valor válido.");
    messages.SetUnknownValueIsInvalidAccessor(field => $"El valor de {field} no es válido.");
    messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor no es válido.");
    messages.SetValueIsInvalidAccessor(value => $"«{value}» no es un valor válido.");
    messages.SetValueMustBeANumberAccessor(field => $"{field} tiene que ser un número.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "Tiene que ser un número.");
    messages.SetValueMustNotBeNullAccessor(_ => "Este campo es obligatorio.");
    messages.SetMissingBindRequiredValueAccessor(field => $"Falta el valor de {field}.");
    messages.SetMissingKeyOrValueAccessor(() => "Falta un valor.");
    messages.SetMissingRequestBodyRequiredValueAccessor(() => "Falta el contenido de la petición.");
});
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("ResidApp")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ResidApp'. En desarrollo, configúrala con " +
        "\"dotnet user-secrets set ConnectionStrings:ResidApp <cadena>\" desde src/ResidApp.Web; nunca en appsettings.json.");
DapperDateOnlyTypeHandler.Register();
// ADR 0008: cada conexión lleva el sujeto verificado y el ámbito activo para la seguridad por filas de SQL (0030).
builder.Services.AddScoped<ITenantContext, RequestTenantContext>();
builder.Services.AddScoped(services => new SqlConnectionFactory(connectionString, services.GetRequiredService<ITenantContext>()));

builder.Services.AddScoped<IResidentRepository, SqlResidentRepository>();
builder.Services.AddScoped<IBaselineRepository, SqlBaselineRepository>();
builder.Services.AddScoped<IAuthorizationEvidenceProvider, SqlAuthorizationEvidenceProvider>();
builder.Services.AddScoped<IProfileScopeDirectoryProvider, SqlProfileScopeDirectoryProvider>();
builder.Services.AddScoped<IAssignedResidentDirectory, SqlAssignedResidentDirectory>();
builder.Services.AddScoped<IEnfermeriaResidentDirectory, SqlEnfermeriaResidentDirectory>();
builder.Services.AddScoped<IDailyClosureRepository, SqlDailyClosureRepository>();
builder.Services.AddScoped<IClinicalEventRepository, SqlClinicalEventRepository>();
builder.Services.AddScoped<IChangeInboxDirectory, SqlChangeInboxDirectory>();
builder.Services.AddScoped<ISupervisionDirectory, SqlSupervisionDirectory>();
builder.Services.AddScoped<ITransferHistoryAccess, SqlTransferHistoryAccess>();
builder.Services.AddScoped<IAdministracionResidentDirectory, SqlAdministracionResidentDirectory>();
builder.Services.AddScoped<IResidentIdentityRepository, SqlResidentIdentityRepository>();
builder.Services.AddScoped<IResidentPhotoRepository, SqlResidentPhotoRepository>();
builder.Services.AddScoped<IResidentFamilyRepository, SqlResidentFamilyRepository>();
builder.Services.AddScoped<IResidentTransferRepository, SqlResidentTransferRepository>();
builder.Services.AddScoped<IResidentStatusRepository, SqlResidentStatusRepository>();
builder.Services.AddScoped<IResidentStatusDirectory, SqlResidentStatusDirectory>();
builder.Services.AddScoped<IProfessionalAccountDirectory, SqlProfessionalAccountDirectory>();
builder.Services.AddScoped<IProfessionalAccountRepository, SqlProfessionalAccountRepository>();
builder.Services.AddScoped<ICenterStructureDirectory, SqlCenterStructureDirectory>();
builder.Services.AddScoped<ICenterStructureRepository, SqlCenterStructureRepository>();
builder.Services.AddScoped<ICenterBrandingDirectory, SqlCenterBrandingDirectory>();
builder.Services.AddScoped<ICenterBrandingRepository, SqlCenterBrandingRepository>();
builder.Services.AddScoped<IPlatformCenterDirectory, SqlPlatformCenterDirectory>();
builder.Services.AddScoped<IPlatformCenterRepository, SqlPlatformCenterRepository>();
builder.Services.AddScoped<IAdministrativeAuditDirectory, SqlAdministrativeAuditDirectory>();
builder.Services.AddScoped<ISchedulingDirectory, SqlSchedulingDirectory>();
builder.Services.AddScoped<ISchedulingRepository, SqlSchedulingRepository>();
builder.Services.AddScoped<ISchedulePlanDirectory, SqlSchedulePlanDirectory>();
builder.Services.AddScoped<ISchedulePlanRepository, SqlSchedulePlanRepository>();
builder.Services.AddScoped<IOrganizationDirectory, SqlOrganizationDirectory>();
builder.Services.AddScoped<IOrganizationRepository, SqlOrganizationRepository>();
builder.Services.AddScoped<INursingAssessmentRepository, SqlNursingAssessmentRepository>();
builder.Services.AddScoped<IMedicalAssessmentRepository, SqlMedicalAssessmentRepository>();
builder.Services.AddScoped<IMedicalIndicationRepository, SqlMedicalIndicationRepository>();
builder.Services.AddScoped<IReferenceRangeRepository, SqlReferenceRangeRepository>();
builder.Services.AddScoped<IReferralReportRepository, SqlReferralReportRepository>();
builder.Services.AddScoped<IAssessmentCorrectionRepository, SqlAssessmentCorrectionRepository>();
// COR-01: ventana de corrección de las valoraciones, global hasta que Administración la configure por centro.
builder.Services.AddSingleton(new AssessmentCorrectionSettings(TimeSpan.FromHours(
    builder.Configuration.GetValue<double?>("Correccion:VentanaHoras")
    ?? throw new InvalidOperationException("Falta Correccion:VentanaHoras en appsettings.json."))));
builder.Services.AddScoped<CorrectNursingAssessment>();
builder.Services.AddScoped<CorrectMedicalAssessment>();
builder.Services.AddScoped<RectifyAssessment>();
builder.Services.AddSingleton<IReferralReportPdfRenderer, ReferralReportPdfRenderer>();
// ADR 0007, incremento I0: sesión con cookie cifrada por Data Protection y validada contra dbo.sesiones en cada petición
// (SessionCookieEvents). La abren el inicio de sesión con contraseña (AccesoController) y con passkey (PasskeyController).
var sessionPolicy = new SessionPolicy(
    TimeSpan.FromMinutes(builder.Configuration.GetValue<int?>("Sesion:InactividadMinutos")
        ?? throw new InvalidOperationException("Falta Sesion:InactividadMinutos en appsettings.json.")),
    TimeSpan.FromHours(builder.Configuration.GetValue<int?>("Sesion:MaximoHoras")
        ?? throw new InvalidOperationException("Falta Sesion:MaximoHoras en appsettings.json.")));
builder.Services.AddSingleton(sessionPolicy);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISessionStore, SqlSessionStore>();
builder.Services.AddScoped<SessionCookieEvents>();
builder.Services.AddScoped<ISessionIdentityProvider, CookieSessionIdentityProvider>();
builder.Services.AddScoped<SessionSignIn>();
builder.Services.AddScoped<IAccessEventLog, SqlAccessEventLog>();
builder.Services.AddSingleton<PendingAccessCookie>();
builder.Services.AddSingleton<InvitationCookie>();
builder.Services.AddSingleton<AccessFailureThrottle>();
// ADR 0007, incremento I2: invitaciones de acceso de un solo uso que entregan Administración y Plataforma.
builder.Services.AddScoped<IAccessInvitationRepository, SqlAccessInvitationRepository>();
builder.Services.AddScoped<AccessInvitationsApplicationService>();
builder.Services.AddScoped<IAccountLinkRepository, SqlAccountLinkRepository>();
builder.Services.AddScoped<AccountLinksApplicationService>();
builder.Services.Configure<AccesoOptions>(builder.Configuration.GetSection(AccesoOptions.Section));
// ADR 0007, incremento I1: ASP.NET Core Identity solo para las credenciales (contraseña, bloqueo, TOTP y códigos de recuperación) sobre
// dbo.credenciales; el usuario es la cuenta y su nombre, sujeto_externo. No se usa SignInManager ni su cookie: la sesión es la de I0.
builder.Services.AddIdentityCore<CredentialAccount>(options =>
    {
        options.Password.RequiredLength = AccesoOptions.LongitudMinimaContrasena;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
        options.Lockout.MaxFailedAccessAttempts = AccesoOptions.IntentosAntesDeBloquear;
        options.Lockout.DefaultLockoutTimeSpan = AccesoOptions.DuracionBloqueo;
        options.Lockout.AllowedForNewUsers = true;
        // sujeto_externo lo valida Administración al crear la cuenta (0023); aquí no se restringe.
        options.User.AllowedUserNameCharacters = string.Empty;
    })
    .AddUserStore<SqlCredentialStore>()
    .AddTokenProvider<AuthenticatorTokenProvider<CredentialAccount>>(TokenOptions.DefaultAuthenticatorProvider);
builder.Services.AddScoped<ILookupNormalizer, SubjectLookupNormalizer>();
// ADR 0007, incremento I4: passkeys (WebAuthn). Sin SignInManager (AddIdentityCore no registra el manejador): el estado de cada ceremonia
// lo guarda PasskeyStateCookie. El dominio va explícito por entorno (Acceso:PasskeyDominio); vacío, las passkeys están desactivadas.
builder.Services.AddScoped<IPasskeyHandler<CredentialAccount>, PasskeyHandler<CredentialAccount>>();
builder.Services.Configure<IdentityPasskeyOptions>(options =>
{
    var passkeyDomain = builder.Configuration[$"{AccesoOptions.Section}:{nameof(AccesoOptions.PasskeyDominio)}"]?.Trim();
    options.ServerDomain = passkeyDomain;
    options.UserVerificationRequirement = "required";
    options.ResidentKeyRequirement = "required";
    options.ValidateOrigin = context => ValueTask.FromResult(
        !context.CrossOrigin && !string.IsNullOrEmpty(passkeyDomain)
        && PasskeyOrigin.IsAllowed(context.Origin, passkeyDomain, builder.Environment.IsDevelopment()));
});
builder.Services.AddSingleton<PasskeyStateCookie>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = SessionAuthentication.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = sessionPolicy.IdleTimeout;
        options.SlidingExpiration = true;
        options.LoginPath = "/Acceso/Entrar";
        options.EventsType = typeof(SessionCookieEvents);
    });
// Toda pantalla exige sesión salvo las marcadas con [AllowAnonymous] (Inicio, Manual, inicio de sesión, passkeys, clave de CJ)
// y los estáticos.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AccesoOptions.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IOptions<AccesoOptions>>().Value.IntentosPorMinuto,
            Window = TimeSpan.FromMinutes(1),
        }));
    // El dictado cuesta dinero y envía audio fuera: se limita por cuenta (la sesión), no por IP, que en una residencia es la misma para todas.
    options.AddPolicy(IaOptions.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(SessionAuthentication.SubjectClaim)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "sin-cuenta",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IOptions<IaOptions>>().Value.PeticionesPorMinuto,
            Window = TimeSpan.FromMinutes(1),
        }));
});
// Hallazgo A2: fuera de Development todas las cookies llevan Secure (sesión, ámbito, acceso clínico, antiforgery, TempData, clave de CJ).
// En Development se mantiene según la petición, para el perfil local http y el TestServer de las pruebas funcionales.
builder.Services.AddOptions<CookiePolicyOptions>().Configure<IWebHostEnvironment>((options, environment) =>
    options.Secure = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
// App Service termina el TLS en su balanceador, que llega desde redes privadas: sin esto la app no sabría que la petición era HTTPS
// (documentación de App Service, «Detect HTTPS session»), no enviaría HSTS y el límite de intentos contaría a todos como una sola IP.
// En Linux llega además desde la red de enlace local (medido en Azure dev el 2026-10-21: ::ffff:169.254.130.1), que la documentación no cita.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16",
                 "::ffff:10.0.0.0/104", "::ffff:172.16.0.0/108", "::ffff:192.168.0.0/112", "::ffff:169.254.0.0/112" })
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});
// La clave de los documentos de CJ (/pendientes-cj): un freno para el desarrollo, ver PendientesCjAccess.
builder.Services.AddSingleton<PendientesCjAccess>();

builder.Services.AddScoped<CreateResident>();
builder.Services.AddScoped<SignBaseline>();
// Declaración de acceso clínico de Dirección (CJ, 2026-10-06): 1 hora, global hasta que se ajuste por centro.
builder.Services.AddSingleton(new ClinicalAccessSettings(
    builder.Configuration.GetValue<int?>("AccesoClinico:DuracionMinutos")
    ?? throw new InvalidOperationException("Falta AccesoClinico:DuracionMinutos en appsettings.json.")));
builder.Services.AddScoped<ReadDirectionBaseline>();
builder.Services.AddScoped<DownloadDirectionReferralReport>();
builder.Services.AddScoped<ClinicalAccessDeclarations>();
builder.Services.AddScoped<TransferHistoryAccess>();
builder.Services.AddScoped<ListActiveProfileScopes>();
builder.Services.AddScoped<ListActiveScopeUnits>();
builder.Services.AddScoped<ListActiveScopeLocations>();
builder.Services.AddScoped<ILocationOptionsDirectory, SqlLocationOptionsDirectory>();
builder.Services.AddScoped<ListActiveScopePermissions>();
builder.Services.AddScoped<CreateBaselineDraft>();
builder.Services.AddScoped<LoadBaselineDraft>();
builder.Services.AddScoped<SaveBaselineDraftArea>();
builder.Services.AddScoped<SaveBaselineDraftBarthel>();
builder.Services.AddScoped<CancelBaselineDraft>();
builder.Services.AddScoped<ResidentBaselineApplicationService>();

builder.Services.AddScoped<ListAssignedResidents>();
builder.Services.AddScoped<FindAssignedResident>();
builder.Services.AddScoped<ReadCurrentBaseline>();
builder.Services.AddScoped<RegisterDailyClosure>();
builder.Services.AddScoped<RegisterDailyChange>();
builder.Services.AddScoped<SuggestDailyChangeFromVoice>();
builder.Services.AddScoped<AuxiliarApplicationService>();

// IA (ADR 0011): un solo proveedor autorizado (Mistral) detrás de dos puertos. Sin IA:Mistral:ApiKey el dictado está desactivado.
builder.Services.Configure<IaOptions>(builder.Configuration.GetSection(IaOptions.Section));
builder.Services.AddHttpClient<MistralClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<IaOptions>>().Value;
    client.BaseAddress = new Uri(MistralOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.Mistral.TiempoEsperaSegundos);
    if (!string.IsNullOrWhiteSpace(options.Mistral.ApiKey))
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.Mistral.ApiKey);
    }
});
builder.Services.AddTransient<ISpeechTranscriber>(services => services.GetRequiredService<MistralClient>());
builder.Services.AddTransient<IStructuredGenerator>(services => services.GetRequiredService<MistralClient>());
builder.Services.AddScoped<IVoiceDictationAudit, SqlVoiceDictationAudit>();

builder.Services.AddScoped<ListScopeResidents>();
builder.Services.AddScoped<FindScopeResident>();
builder.Services.AddScoped<FindEmergencyContact>();
builder.Services.AddScoped<IMilestoneFactDirectory>(sp => sp.GetRequiredService<IChangeInboxDirectory>());
builder.Services.AddScoped<IProcessDeadlineRepository, SqlProcessDeadlineRepository>();
builder.Services.AddScoped<ProcessDeadlinesApplicationService>();
builder.Services.AddScoped<ListMilestoneWarnings>();
builder.Services.AddScoped<RegisterClinicalEvent>();
builder.Services.AddScoped<ListPendingChanges>();
builder.Services.AddScoped<FindPendingChangeDetail>();
builder.Services.AddScoped<StartNursingAssessment>();
builder.Services.AddScoped<SaveNursingAssessment>();
builder.Services.AddScoped<CloseClinicalEvent>();
builder.Services.AddScoped<ListPendingFamilyCommunications>();
builder.Services.AddScoped<StartFollowUp>();
builder.Services.AddScoped<RecordFollowUpAction>();
builder.Services.AddScoped<ListFollowUps>();
builder.Services.AddScoped<ListOpenEscalations>();
builder.Services.AddScoped<ListOpenEvents>();
builder.Services.AddScoped<ListTransferTeams>();
builder.Services.AddScoped<ITransferTeamDirectory, SqlTransferTeamDirectory>();
builder.Services.AddScoped<EscalateClinicalEvent>();
builder.Services.AddScoped<ListPendingIndications>();
builder.Services.AddScoped<ListEventsReadyToClose>();
builder.Services.AddScoped<RecordIndicationProgress>();
builder.Services.AddScoped<ListClosedEvents>();
builder.Services.AddScoped<ReadBaselineHistory>();
builder.Services.AddScoped<ReadResidentTimeline>();
builder.Services.AddScoped<EnfermeriaApplicationService>();

builder.Services.AddScoped<ListEscalations>();
builder.Services.AddScoped<FindEscalationDetail>();
builder.Services.AddScoped<StartMedicalAssessment>();
builder.Services.AddScoped<SaveMedicalAssessment>();
builder.Services.AddScoped<RegisterMedicalIndication>();
builder.Services.AddScoped<ListMedicalIndications>();
builder.Services.AddScoped<CloseMedicalEvent>();
builder.Services.AddScoped<StartMedicalFollowUp>();
builder.Services.AddScoped<RecordMedicalFollowUpAction>();
builder.Services.AddScoped<ListMedicalFollowUps>();
builder.Services.AddScoped<ActivateUrgentProtocol>();
builder.Services.AddScoped<RecordUrgentProtocolEntry>();
builder.Services.AddScoped<ListUrgentProtocols>();
builder.Services.AddScoped<ActivateMedicalUrgentProtocol>();
builder.Services.AddScoped<RecordMedicalUrgentProtocolEntry>();
builder.Services.AddScoped<ListMedicalUrgentProtocols>();
builder.Services.AddScoped<SignReferralReport>();
builder.Services.AddScoped<RecordFamilyCallAttempt>();
builder.Services.AddScoped<SignMedicalReferralReport>();
builder.Services.AddScoped<RecordMedicalFamilyCallAttempt>();
builder.Services.AddScoped<FindResidentIdentification>();
builder.Services.AddScoped<DownloadReferralReport>();
builder.Services.AddScoped<MedicinaApplicationService>();
builder.Services.AddScoped<DireccionApplicationService>();
builder.Services.AddScoped<AdministrationAccessResolver>();
builder.Services.AddScoped<AdministracionApplicationService>();
builder.Services.AddScoped<ResidentTransferApplicationService>();
builder.Services.AddScoped<ResidentStatusApplicationService>();
builder.Services.AddScoped<ResidentPhotoApplicationService>();
builder.Services.AddScoped<SqlAdministrationScope>();
builder.Services.AddScoped<IAdministrationScopeDirectory>(sp => sp.GetRequiredService<SqlAdministrationScope>());
builder.Services.AddScoped<IAdministrationScopeRepository>(sp => sp.GetRequiredService<SqlAdministrationScope>());
builder.Services.AddScoped<AdministrationScopeApplicationService>();
builder.Services.AddScoped<SqlFamilyCommunicationPublication>();
builder.Services.AddScoped<IFamilyCommunicationDirectory>(sp => sp.GetRequiredService<SqlFamilyCommunicationPublication>());
builder.Services.AddScoped<IFamilyCommunicationPublisher>(sp => sp.GetRequiredService<SqlFamilyCommunicationPublication>());
builder.Services.AddScoped<IFamilyCommunicationAmender>(sp => sp.GetRequiredService<SqlFamilyCommunicationPublication>());
builder.Services.AddScoped<FamilyCommunicationPublicationApplicationService>();
builder.Services.AddScoped<IFamilyCommunicationScheduleRepository, SqlFamilyCommunicationScheduleRepository>();
builder.Services.AddScoped<FamilyCommunicationScheduleApplicationService>();
builder.Services.AddScoped<IFamilyCommunicationCorrector, SqlFamilyCommunicationCorrection>();
builder.Services.AddScoped<CorrectFamilyCommunication>();
builder.Services.AddScoped<IFamilyPortalReader, SqlFamilyPortalReader>();
builder.Services.AddScoped<FamilyPortalApplicationService>();
builder.Services.AddScoped<IFamilyAppointments, SqlFamilyAppointments>();
builder.Services.AddScoped<FamilyAppointmentsApplicationService>();
builder.Services.AddScoped<IAppointmentServiceRepository, SqlAppointmentServiceRepository>();
builder.Services.AddScoped<AppointmentServiceApplicationService>();
builder.Services.AddScoped<IFamilyAppointmentRequests, SqlFamilyAppointmentRequests>();
builder.Services.AddScoped<FamilyAppointmentRequestsApplicationService>();
builder.Services.AddScoped<IAppointmentRequestRepository, SqlAppointmentRequestRepository>();
builder.Services.AddScoped<AppointmentRequestsApplicationService>();
builder.Services.AddScoped<IAdministrationOverviewReader, SqlAdministrationOverview>();
builder.Services.AddScoped<AdministrationOverviewApplicationService>();
builder.Services.AddScoped<ICenterLayoutDirectory, SqlCenterLayoutDirectory>();
builder.Services.AddScoped<ICenterLayoutRepository, SqlCenterLayoutRepository>();
builder.Services.AddScoped<AdministracionEstructuraApplicationService>();
builder.Services.AddScoped<AdministracionCentroApplicationService>();
// Importación y exportación por ficheros (ADR 0010).
builder.Services.AddSingleton<ITableFileReader, ExcelTableFileReader>();
builder.Services.AddScoped<IExchangeDirectory, SqlExchangeDirectory>();
builder.Services.AddScoped<IExchangeRepository, SqlExchangeRepository>();
builder.Services.AddScoped<AdministracionIntercambioApplicationService>();
builder.Services.AddScoped<AdministracionTurnosApplicationService>();
builder.Services.AddScoped<AdministracionOrganigramaApplicationService>();
builder.Services.AddScoped<OrganigramaApplicationService>();
builder.Services.AddScoped<PlatformApplicationService>();

builder.Services.AddScoped<ReferenceRangesApplicationService>();

// Fechas y números siempre en es-ES, sea cual sea la cultura del servidor: en el contenedor Linux de Azure
// es la invariante y las fechas salían como MM/dd/yyyy. Es la única cultura admitida, así que el
// Accept-Language del navegador no la cambia. Los patrones cortos se fijan porque ICU (Linux) usa
// d/M/yyyy H:mm y Windows dd/MM/yyyy HH:mm.
var spanish = new CultureInfo("es-ES");
spanish.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
spanish.DateTimeFormat.ShortTimePattern = "HH:mm";
spanish = CultureInfo.ReadOnly(spanish);
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(spanish);
    options.SupportedCultures = [spanish];
    options.SupportedUICultures = [spanish];
    options.ApplyCurrentCultureToResponseHeaders = true;
});

var app = builder.Build();

// Las horas se calculan con la zona del servidor: si no es Europe/Madrid, en producción no arranca y en desarrollo avisa.
ServerTimeZone.Verify(app.Environment, app.Logger);
// Un proveedor de IA que no esté en la lista autorizada (ADR 0011) impide arrancar.
IaOptions.Validate(app.Services.GetRequiredService<IOptions<IaOptions>>().Value);

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // La redirección de HTTP a HTTPS la hace App Service (httpsOnly, que fija el CI); aquí solo se pide al navegador que no vuelva a HTTP.
    app.UseHsts();
}
app.UseCookiePolicy();
app.UseRequestLocalization();
// Los documentos de CJ (y sus respuestas) piden la clave: sin ella, las páginas redirigen al formulario y los .json dan 401 (el script de
// cada documento lo ignora en silencio). Va antes de servir los estáticos.
app.UseWhen(context => context.Request.Path.StartsWithSegments(PendientesCjAccess.Prefix), branch => branch.Use(async (context, next) =>
{
    if (context.RequestServices.GetRequiredService<PendientesCjAccess>().HasAccess(context.Request))
    {
        await next(context);
    }
    else if (context.Request.Path.Value!.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
    else
    {
        context.Response.Redirect("/AccesoCj?returnUrl=" + Uri.EscapeDataString(context.Request.Path + context.Request.QueryString));
    }
}));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "pendientes-cj")),
    RequestPath = "/pendientes-cj"
});
app.UseRouting();

app.UseAuthentication();
// Después de autenticar: la política del dictado limita por cuenta y necesita saber quién es (las de acceso, por IP, no).
app.UseRateLimiter();
app.UseAuthorization();

// Con la política global, los estáticos (CSS, JS, manifest.json, sw.js, flujo-de-trabajo.html) también pedirían sesión.
app.MapStaticAssets().Add(endpoint => endpoint.Metadata.Add(new AllowAnonymousAttribute()));
// Un documento de CJ que ya no existe da 404, no el inicio de sesión de la app: esa carpeta tiene su propia clave.
app.MapFallback("/pendientes-cj/{**path}", () => Results.NotFound()).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

/// <summary>Marca requerida por WebApplicationFactory&lt;Program&gt; (ResidApp.FunctionalTests): sin esta
/// declaración parcial, el Program implícito de los top-level statements no es accesible desde otro
/// ensamblado.</summary>
public partial class Program;
