using System.Globalization;
using Microsoft.AspNetCore.Localization;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Pdf;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("ResidApp")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ResidApp'. En desarrollo, configúrala con " +
        "\"dotnet user-secrets set ConnectionStrings:ResidApp <cadena>\" desde src/ResidApp.Web; nunca en appsettings.json.");
DapperDateOnlyTypeHandler.Register();
builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));

builder.Services.AddScoped<IResidentRepository, SqlResidentRepository>();
builder.Services.AddScoped<IBaselineRepository, SqlBaselineRepository>();
builder.Services.AddScoped<IAuthorizationEvidenceProvider, SqlAuthorizationEvidenceProvider>();
builder.Services.AddScoped<IProfileScopeDirectoryProvider, SqlProfileScopeDirectoryProvider>();
builder.Services.AddScoped<IAssignedResidentDirectory, SqlAssignedResidentDirectory>();
builder.Services.AddScoped<IEnfermeriaResidentDirectory, SqlEnfermeriaResidentDirectory>();
builder.Services.AddScoped<IDailyClosureRepository, SqlDailyClosureRepository>();
builder.Services.AddScoped<IClinicalEventRepository, SqlClinicalEventRepository>();
builder.Services.AddScoped<IChangeInboxDirectory, SqlChangeInboxDirectory>();
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

builder.Services.AddScoped<CreateResident>();
builder.Services.AddScoped<SignBaseline>();
builder.Services.AddScoped<ReadDirectionBaseline>();
builder.Services.AddScoped<ListActiveProfileScopes>();
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
