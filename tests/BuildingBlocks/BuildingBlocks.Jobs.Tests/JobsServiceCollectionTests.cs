using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Jobs.Tests;

public sealed class JobsServiceCollectionTests
{
    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDb>(options => options.UseNpgsql("Host=localhost"));
        services.AddJobs<TestDb>(new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Jobs"));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void RegistersTheQueueScopedTheWakeUpSingletonAndTheHostedWorker()
    {
        using var provider = Build([]);

        using var scope = provider.CreateScope();
        Assert.IsType<JobQueue<TestDb>>(scope.ServiceProvider.GetRequiredService<IJobQueue>());
        Assert.Same(provider.GetRequiredService<IJobWakeUp>(), provider.GetRequiredService<IJobWakeUp>());
        Assert.Single(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void BindsOptionsFromTheGivenSection()
    {
        using var provider = Build(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Jobs:LeaseDuration"] = "00:00:07",
            ["Jobs:SafetyNetPollInterval"] = "01:00:00",
            ["Jobs:MaxAttempts"] = "3",
            ["Jobs:MaxBackoff"] = "00:00:10",
        });

        var options = provider.GetRequiredService<IOptions<JobOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(7), options.LeaseDuration);
        Assert.Equal(TimeSpan.FromHours(1), options.SafetyNetPollInterval);
        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(10), options.MaxBackoff);
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        using var provider = Build(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Jobs:LeaseDuration"] = "00:00:00.5" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JobOptions>>().Value);
    }

    [Fact]
    public void InvalidOptionsFailHostStartup()
    {
        using var provider = Build(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Jobs:SafetyNetPollInterval"] = "00:00:00" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }
}
