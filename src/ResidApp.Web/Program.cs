using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Authorization;
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
builder.Services.AddScoped<EnfermeriaApplicationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
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
