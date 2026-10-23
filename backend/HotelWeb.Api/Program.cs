using HotelWeb.Api.Datos;
using HotelWeb.Api.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// los enum viajan como texto ("Ocupada", "Libre"...), no como numero
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<Conexiones>();
builder.Services.AddSingleton<RepositorioUsuarios>();
builder.Services.AddSingleton<HotelWeb.Api.Rack.RepositorioRack>();
builder.Services.AddSingleton<HotelWeb.Api.Catalogos.RepositorioPrecios>();
builder.Services.AddSingleton<HotelWeb.Api.Catalogos.RepositorioHabitaciones>();
builder.Services.AddSingleton<HotelWeb.Api.Catalogos.RepositorioTipos>();
builder.Services.AddSingleton<HotelWeb.Api.Catalogos.RepositorioServicios>();
builder.Services.AddSingleton<HotelWeb.Api.Catalogos.RepositorioClientes>();
builder.Services.AddSingleton<HotelWeb.Api.Reservas.RepositorioReservas>();
builder.Services.AddSingleton<HotelWeb.Api.Hospedaje.RepositorioCheckIn>();
builder.Services.AddSingleton<HotelWeb.Api.Hospedaje.RepositorioHospedaje>();
builder.Services.AddSingleton<HotelWeb.Api.Hospedaje.RepositorioCuentas>();
builder.Services.AddSingleton<PermisosWeb>();

// ---- sesion: token JWT; todo la pide salvo el ingreso y el ping ----
var tokens = new Tokens(Tokens.ClaveConfigurada(builder.Configuration, builder.Environment.IsDevelopment()));
builder.Services.AddSingleton(tokens);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = Tokens.Emisor,
        ValidAudience = Tokens.Emisor,
        IssuerSigningKey = tokens.Clave,
        NameClaimType = "name",
        ClockSkew = TimeSpan.FromMinutes(1),
    };
});
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// El frontend (Vite en desarrollo, o el sitio de IIS) llama a esta API desde otro origen.
var origenes = (builder.Configuration["Cors:Origenes"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// la pagina web (frontend compilado en wwwroot): un solo sitio en IIS para la pagina y la API
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// cualquier ruta que no sea de la API es de la pagina (React decide que mostrar)
// nonfile: los archivos (.js, .css, .svg) los sirve UseStaticFiles; y las que empiezan con api/ tampoco
// (una ruta de la API que no existe no debe devolver la pagina)
app.MapFallbackToFile("{**ruta:nonfile:regex(^(?!api/).*$)}", "index.html").AllowAnonymous();

app.Run();

// para las pruebas de la API completa (WebApplicationFactory)
public partial class Program;
