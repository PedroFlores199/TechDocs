using Hangfire;
using TechDocs.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTechDocs(builder.Configuration);
builder.Services.AddHangfire(c => c.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings().UseSqlServerStorage(builder.Configuration.GetConnectionString("SqlServer")));
builder.Services.AddHangfireServer();
await builder.Build().RunAsync();
