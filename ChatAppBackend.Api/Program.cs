using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using WebService.Filters;
using WebService.Helpers;
using WebService.Helpers.Interfaces;
using WebService.Hubs;
using WebService.Middlewares;
using WebService.Models;
using WebService.Models.Entities;
using WebService.Models.Options;
using WebService.Providers;
using WebService.Providers.Interfaces;
using WebService.Repositories;
using WebService.Repositories.Interfaces;
using WebService.Services;
using WebService.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

#region Services

builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IPresenceService, PresenceService>();
builder.Services.AddScoped<IFriendshipService, FriendshipService>();

#endregion

#region Helpers

builder.Services.AddScoped<IAuthHelper, AuthHelper>();
builder.Services.AddScoped<IAuthContextProvider, AuthContextProvider>();

builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration);

#endregion

#region Middlewares

builder.Services.AddScoped<ErrorHandlingMiddleWare>();

#endregion

#region Kafka Queue

var queueSettingSection = builder.Configuration.GetSection("KafkaOptions");
builder.Services.Configure<KafkaOptions>(queueSettingSection);

builder.Services.AddScoped<INotificationProducerService, NotificationProducerService>();
builder.Services.AddHostedService<NotificationConsumerService>();

#endregion

#region Database and Identity

var appDbSettingSection = builder.Configuration.GetSection("ChatAppDbOptions");
var appDbSettings = appDbSettingSection.Get<ChatAppDbOptions>();
builder.Services.Configure<ChatAppDbOptions>(appDbSettingSection);

builder.Services.AddSingleton<IMongoClient>(new MongoClient(appDbSettings!.ConnectionString));
builder.Services.AddScoped<IMongoDatabase>(provider =>
    provider.GetRequiredService<IMongoClient>().GetDatabase(appDbSettings!.DatabaseName));

builder.Services.AddScoped(typeof(IMongoRepository<>), typeof(MongoRepository<>));

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

builder.Services
    .AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis")!);

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

builder.Services.AddCors(
    options =>
    {
        options.AddPolicy("AllowAll",
            policy =>
            {
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
    });

builder.Services.Configure<HostOptions>(c =>
{
    c.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

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

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<ErrorHandlingMiddleWare>();

app.MapHub<ChatHub>("/ChatHub");

app.MapGet("/", context =>
{
    context.Response.Redirect("/client.html");
    return Task.CompletedTask;
});

app.Run();