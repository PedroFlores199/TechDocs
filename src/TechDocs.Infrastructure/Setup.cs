using System.Security.Cryptography;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechDocs.Application;
using TechDocs.Domain;

namespace TechDocs.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddTechDocs(this IServiceCollection services, IConfiguration config)
    {
        var local = config["Storage:Mode"] == "Local";
        services.AddDbContext<TechDocsDb>(o =>
        {
            if (local) o.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=techdocs.db");
            else o.UseSqlServer(config.GetConnectionString("SqlServer"));
        });
        if (local)
        {
            services.AddSingleton<IContentStore>(new FileContentStore(config["Storage:Directory"] ?? "app_data/versions"));
            services.AddScoped<ISearchIndex, LocalSearchIndex>();
        }
        else
        {
            services.AddSingleton<IContentStore>(new MongoContentStore(config.GetConnectionString("Mongo")!));
            services.AddHttpClient<ElasticsearchIndex>(c => c.BaseAddress = new Uri(config["Search:Url"] ?? "http://localhost:9200/"));
            services.AddScoped<ISearchIndex>(s => s.GetRequiredService<ElasticsearchIndex>());
        }
        services.AddHttpClient("embeddings", c => c.Timeout = TimeSpan.FromMinutes(3));
        services.AddSingleton<EmbeddingClient>(s => new EmbeddingClient(s.GetRequiredService<IHttpClientFactory>().CreateClient("embeddings"), config));
        services.AddScoped<SemanticSearch>();
        services.AddScoped<DocumentService>(); services.AddScoped<ImportService>(); services.AddScoped<ManualExporter>();
        return services;
    }
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(salt) + "." + Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32));
    }
    public static bool VerifyPassword(string password, string encoded)
    {
        var values = encoded.Split('.');
        if (values.Length != 2) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(values[1]), Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(values[0]), 210_000, HashAlgorithmName.SHA256, 32));
    }
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TechDocsDb>();
        await db.Database.EnsureCreatedAsync(ct);
        if (scope.ServiceProvider.GetRequiredService<ISearchIndex>() is ElasticsearchIndex elastic) await elastic.InitializeAsync(ct);
        if (await db.Users.AnyAsync(ct)) return;
        var users = Enum.GetValues<Role>().Select(r => new User { Email = r.ToString().ToLowerInvariant() + "@techdocs.demo", Name = r.ToString(), Role = r, PasswordHash = HashPassword("Demo1234!") }).ToArray();
        db.Users.AddRange(users);
        var product = new Product { Name = "Atlas R-240 · Unidad ferroviaria", Code = "ATL-R240" };
        db.Products.Add(product);
        var systems = new[] { "Hidráulica", "Frenado", "Tracción", "Climatización", "Estructura", "Señalización" }.Select(n => new TechnicalSystem { ProductId = product.Id, Name = n }).ToArray();
        db.Systems.AddRange(systems);
        var supplier = new Supplier { Name = "Componentes Atlas · Ficticio" }; db.Suppliers.Add(supplier);
        var names = new[] { "Junta tórica 12 mm", "Filtro hidráulico", "Válvula de seguridad", "Disco de freno", "Sensor de presión", "Bomba de circulación" };
        var faker = new Faker("es"); faker.Random = new Randomizer(42);
        var parts = Enumerable.Range(0, 36).Select(i => new Part { Code = $"ATL-{i + 1:0000}", Name = names[i % names.Length] + (i < 6 ? "" : $" · Serie {i / 6 + 1}"), Specification = $"Catálogo ficticio. Material {faker.PickRandom("acero inoxidable", "elastómero NBR", "aluminio")}; inspección cada {faker.Random.Int(2, 8) * 1000} km.", SystemId = systems[i % systems.Length].Id, SupplierId = supplier.Id }).ToArray();
        db.Parts.AddRange(parts);
        await db.SaveChangesAsync(ct);
        var svc = scope.ServiceProvider.GetRequiredService<DocumentService>();
        var titles = new[] { "Sustitución de junta tórica del circuito hidráulico", "Cambio del filtro hidráulico principal", "Verificación de la válvula de seguridad", "Inspección del disco de freno", "Calibración del sensor de presión", "Revisión de la bomba de circulación" };
        for (var i = 0; i < 12; i++)
        {
            var p = parts[i];
            var id = await svc.CreateAsync(new($"MP-{i + 1:000}", titles[i % 6] + (i > 5 ? " · Inspección extendida" : ""), systems[i % systems.Length].Id,
            [new("heading", "Preparación y seguridad"), new("warning", "Procedimiento ficticio para demostración. No utilizar en mantenimiento real."), new("step", "Aislar el circuito, comprobar presión cero y registrar el número de serie de la pieza.", p.Code), new("step", $"Retirar {p.Name.ToLowerInvariant()}, limpiar el alojamiento e instalar el recambio indicado en la ficha.", p.Code), new("table", "Control | Criterio\nEstanqueidad | Sin fugas\nIdentificación | Código coincidente"), new("step", "Verificar estanqueidad y registrar el resultado de la intervención.")], [p.Id]), users[0].Id, ct);
            if (i % 3 != 0)
            {
                await svc.TransitionAsync(id, new("submit", 1, null), Role.Redactor, users[0].Id, ct);
                if (i % 3 == 2)
                {
                    await svc.TransitionAsync(id, new("review", 2, null), Role.Revisor, users[1].Id, ct);
                    await svc.TransitionAsync(id, new("approve", 3, null), Role.Aprobador, users[2].Id, ct);
                }
            }
        }
    }
}
