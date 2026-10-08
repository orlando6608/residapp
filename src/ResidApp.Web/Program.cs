using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Pdf;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
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
builder.Services.AddScoped<IAdministracionResidentDirectory, SqlAdministracionResidentDirectory>();
builder.Services.AddScoped<IResidentIdentityRepository, SqlResidentIdentityRepository>();
builder.Services.AddScoped<IResidentFamilyRepository, SqlResidentFamilyRepository>();
builder.Services.AddScoped<IResidentTransferRepository, SqlResidentTransferRepository>();
builder.Services.AddScoped<IResidentStatusRepository, SqlResidentStatusRepository>();
builder.Services.AddScoped<IResidentStatusDirectory, SqlResidentStatusDirectory>();
builder.Services.AddScoped<IProfessionalAccountDirectory, SqlProfessionalAccountDirectory>();
builder.Services.AddScoped<IProfessionalAccountRepository, SqlProfessionalAccountRepository>();
builder.Services.AddScoped<ICenterStructureDirectory, SqlCenterStructureDirectory>();
builder.Services.AddScoped<ICenterStructureRepository, SqlCenterStructureRepository>();
builder.Services.AddScoped<IPlatformCenterDirectory, SqlPlatformCenterDirectory>();
builder.Services.AddScoped<IPlatformCenterRepository, SqlPlatformCenterRepository>();
builder.Services.AddScoped<IAdministrativeAuditDirectory, SqlAdministrativeAuditDirectory>();
builder.Services.AddScoped<ISchedulingDirectory, SqlSchedulingDirectory>();
builder.Services.AddScoped<ISchedulingRepository, SqlSchedulingRepository>();
builder.Services.AddScoped<ISchedulePlanDirectory, SqlSchedulePlanDirectory>();
builder.Services.AddScoped<ISchedulePlanRepository, SqlSchedulePlanRepository>();
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
builder.Services.AddScoped<ISessionIdentityProvider, DevSessionIdentityProvider>();
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
builder.Services.AddScoped<AuxiliarApplicationService>();

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
builder.Services.AddScoped<SqlAdministrationScope>();
builder.Services.AddScoped<IAdministrationScopeDirectory>(sp => sp.GetRequiredService<SqlAdministrationScope>());
builder.Services.AddScoped<IAdministrationScopeRepository>(sp => sp.GetRequiredService<SqlAdministrationScope>());
builder.Services.AddScoped<AdministrationScopeApplicationService>();
builder.Services.AddScoped<SqlFamilyCommunicationPublication>();
builder.Services.AddScoped<IFamilyCommunicationDirectory>(sp => sp.GetRequiredService<SqlFamilyCommunicationPublication>());
builder.Services.AddScoped<IFamilyCommunicationPublisher>(sp => sp.GetRequiredService<SqlFamilyCommunicationPublication>());
builder.Services.AddScoped<AdministrationFamilyCommunicationApplicationService>();
builder.Services.AddScoped<IFamilyCommunicationCorrector, SqlFamilyCommunicationCorrection>();
builder.Services.AddScoped<CorrectFamilyCommunication>();
builder.Services.AddScoped<ICenterLayoutDirectory, SqlCenterLayoutDirectory>();
builder.Services.AddScoped<ICenterLayoutRepository, SqlCenterLayoutRepository>();
builder.Services.AddScoped<AdministracionEstructuraApplicationService>();
builder.Services.AddScoped<AdministracionTurnosApplicationService>();
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
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

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

/// <summary>Marca requerida por WebApplicationFactory&lt;Program&gt; (ResidApp.FunctionalTests): sin esta
/// declaración parcial, el Program implícito de los top-level statements no es accesible desde otro
/// ensamblado.</summary>
public partial class Program;
