using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Shared.Models;
using WebService.Filters;
using WebService.Helpers;
using WebService.Helpers.Interfaces;
using WebService.Middlewares;
using WebService.Models.Entities;
using WebService.Providers;
using WebService.Providers.Interfaces;

var builder = WebApplication.CreateBuilder(args);

#region Helpers

builder.Services.AddScoped<IAuthHelper, AuthHelper>();
builder.Services.AddScoped<IAuthContextProvider, AuthContextProvider>();

#endregion

#region Middlewares

builder.Services.AddScoped<ErrorHandlingMiddleWare>();

#endregion

#region Database and Identity

var appDbSettingSection = builder.Configuration.GetSection("ChatAppDbOptions");
var appDbSettings = appDbSettingSection.Get<ChatAppDbOptions>();
builder.Services.Configure<ChatAppDbOptions>(appDbSettingSection);

builder.Services.AddIdentity<ChatUser, ChatRole>(options =>
    {
        // Password settings
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 1;

        // Lockout settings
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = false;

        // User settings    
        options.User.RequireUniqueEmail = true;
    }).AddMongoDbStores<ChatUser, ChatRole, ObjectId>(
        appDbSettings!.ConnectionString,
        appDbSettings!.DatabaseName)
    .AddDefaultTokenProviders();

#endregion

#region JWT Authentication and Authorization

// JWT settings
var jwtSettingSection = builder.Configuration.GetSection("JwtOptions");
var jwtSettings = jwtSettingSection.Get<JwtOptions>();
builder.Services.Configure<JwtOptions>(jwtSettingSection);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtSettings!.Issuer,
            ValidAudience = jwtSettings!.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings!.Key)),

            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser().Build());

#endregion

#region Config

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(option =>
    {
        option.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Please enter a valid token",
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            BearerFormat = "JWT",
            Scheme = "Bearer"
        });

        option.OperationFilter<AuthResponseOperationFilter>();
    }
);

#endregion

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(option =>
    {
        option.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        option.EnablePersistAuthorization();
    });
}

app.MapControllers();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<ErrorHandlingMiddleWare>();

app.Run();