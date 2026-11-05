using System.Text;
using AMS.Api;
using AMS.Api.Repositories;
using AMS.Api.Services;
using AMS.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Data access ───────────────────────────────────────────────────────────
// EF Core + a SQL Server LocalDB file in development. Swap the connection
// string to the SQLEXPRESS (or any full) instance — nothing else moves.
builder.Services.AddDbContext<AmsDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ── Layered architecture: controller → service → repository → EF Core ────
// Controllers (Api/Controllers) route HTTP and authorize.
// Services (Api/Services) hold every business rule and throw ApiException.
// Repositories (Api/Repositories) are the ONLY layer touching the DbContext.
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IEntryRepository, EntryRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<ICashLedgerRepository, CashLedgerRepository>();
builder.Services.AddScoped<IActivityRepository, ActivityRepository>();

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IEntryService, EntryService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICashLedgerService, CashLedgerService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IInsightsService, InsightsService>();
builder.Services.AddScoped<IReportsService, ReportsService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IActivityService, ActivityService>();

// ── HTTP plumbing ─────────────────────────────────────────────────────────
builder.Services.AddControllers();

// CORS for the Vite dev server (and its production preview port) by
// default; deployed origins ride in via config so the same build serves
// anywhere — Cors:AllowedOrigins (config array) or, on the host, the
// CORS__AllowedOrigins env var (comma-separated, e.g. the Vercel URL).
// The binder will not comma-split a SCALAR value into string[], so the
// env var (always one string) has to be split here: read any child
// entries (the config-array form) AND the section's own scalar value,
// trim each, drop blanks — a paste's stray space must not silently
// disable an origin.
var corsSection = builder.Configuration.GetSection("Cors:AllowedOrigins");
// All origins fold into one list: the child entries (the config-array
// form) plus the section's own scalar — which is how the env var arrives:
// ONE string, several origins packed with commas. The binder does not
// comma-split scalars into string[], so the split happens here.
var rawOrigins = corsSection.GetChildren().Select(c => c.Value)
    .Concat([corsSection.Value])
    .SelectMany(v => v?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? []);
var corsOrigins = new List<string> { "http://localhost:5173", "http://localhost:4173" };
foreach (var raw in rawOrigins)
{
    var origin = raw.Trim();
    if (origin.Length > 0) corsOrigins.Add(origin);
}
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(corsOrigins.ToArray())
    .AllowAnyHeader().AllowAnyMethod()));

// JWT Bearer auth — every business endpoint requires it; /api/users*
// additionally requires the Admin role.
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"] ?? "")),
        };
    });
// Role policy — every write endpoint is tagged [Authorize(Policy = "Writer")],
// so view-only users can browse but never lend, edit or release.
// "Admin" gives UsersController a named parity with the rest.
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Writer", p => p.RequireRole("Admin", "Lender"));
    o.AddPolicy("Admin", p => p.RequireRole("Admin"));
});

// Service-layer business errors ({ message } + status — shape the frontend
// surfaces) without try/catch in every action.
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

// Swagger for debugging the API in the browser during development.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// One log line that settles every CORS mystery: the origins the policy
// ACTUALLY loaded (visible in the host's logs, e.g. Render Events).
app.Logger.LogInformation("CORS allowed origins: {Origins}", string.Join(", ", corsOrigins));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();