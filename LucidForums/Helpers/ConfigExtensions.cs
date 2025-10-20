namespace LucidForums.Helpers;

using Microsoft.Extensions.Options;


public static class ConfigExtensions {
    
    public static TConfig ConfigurePOCO<TConfig>(this IServiceCollection services, IConfigurationSection configuration)
        where TConfig : class, new() {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        
        var config = new TConfig();
        configuration.Bind(config);
        services.AddSingleton(config);
        return config;
    }

    /// <summary>
    /// Registers a POCO class as a Scoped service, resolving via IOptionsMonitor.
    /// This is useful to respond to config changes in real-time.
    /// With Azure Configuration this triggers a reload of the config section.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <typeparam name="TConfig"></typeparam>
    /// <returns></returns>
    public static TConfig ConfigureScopedPOCOFromMonitor<TConfig>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TConfig : class, IConfigSection, new()
    {

        // Register scoped POCO that resolves via IOptionsMonitor
        services.AddScoped<TConfig>(sp =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<TConfig>>();
            return monitor.CurrentValue;
        });
        // Register IOptions<T> from config section
        services.Configure<TConfig>(configuration.GetSection(TConfig.Section));

        // Return the current value immediately
        var initialConfig = new TConfig();
        configuration.GetSection(TConfig.Section).Bind(initialConfig);
        return initialConfig;
    }
    

    
    public static TConfig ConfigurePOCO<TConfig>(this IServiceCollection services, IConfiguration configuration) 
        where TConfig : class, IConfigSection, new()
    {
        var sectionName = TConfig.Section;
        var section = configuration.GetSection(sectionName);
        return services.ConfigurePOCO<TConfig>(section);
    }

    public static TConfig ConfigurePOCO<TConfig>(this IServiceCollection services, ConfigurationManager configuration)
        where TConfig : class, IConfigSection, new()
    { 
        if(configuration is not IConfiguration iConfig)
            throw new ArgumentException("ConfigurationManager must be of type IConfiguration");
      return services.ConfigurePOCO<TConfig>(iConfig);
    }
    
    public static TConfig Configure<TConfig>(this WebApplicationBuilder builder)
        where TConfig : class, IConfigSection, new() {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var sectionName = TConfig.Section;
        return services.ConfigurePOCO<TConfig>(configuration.GetSection(sectionName));
    }
    

    public static TConfig GetConfig<TConfig>(this WebApplicationBuilder builder)
        where TConfig : class, IConfigSection, new() {
        var configuration = builder.Configuration;
        var sectionName = TConfig.Section;
        var section = configuration.GetSection(sectionName).Get<TConfig>();
        return section;
        
    }
    
    public static Dictionary<string, object> GetConfigSection(this IConfiguration configuration, string sectionName) {
        var section = configuration.GetSection(sectionName);
        var result = new Dictionary<string, object>();
        foreach (var child in section.GetChildren()) {
            var key = child.Key;
            var value = child.Value;
            result.Add(key, value);
        }
        
        return result;
    }
    
    public static Dictionary<string, object> GetConfigSection<TConfig>(this WebApplicationBuilder builder)
        where TConfig : class, IConfigSection, new() {
        var configuration = builder.Configuration;
        var sectionName = TConfig.Section;
        return configuration.GetConfigSection(sectionName);
    }
}