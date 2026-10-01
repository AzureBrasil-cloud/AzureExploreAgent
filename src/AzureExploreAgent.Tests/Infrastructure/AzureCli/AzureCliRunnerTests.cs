using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Processes;
using AzureExploreAgent.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Tests.Infrastructure.AzureCli;

public class AzureCliRunnerTests
{
    [Fact]
    public async Task Runs_configured_executable_with_isolated_config_directory_and_default_timeout()
    {
        var configDirectory = Path.Combine(Path.GetTempPath(), "AzureExploreAgent.Tests", Guid.NewGuid().ToString("N"));
        var processRunner = new FakeProcessRunner().Returns(new ProcessResult(0, "[]", string.Empty, TimedOut: false));
        var runner = new AzureCliRunner(processRunner, Options.Create(new AzureCliOptions
        {
            ExecutablePath = "/usr/bin/az",
            ConfigDirectory = configDirectory,
            SubscriptionId = "sub",
            TimeoutSeconds = 45,
        }));

        try
        {
            var result = await runner.RunAsync(["resource", "list"], timeout: null, CancellationToken.None);

            var request = processRunner.Requests.Single();
            Assert.Equal("/usr/bin/az", request.FileName);
            Assert.Equal(["resource", "list"], request.Arguments);
            Assert.Equal(configDirectory, request.Environment[AzureCliRunner.ConfigDirectoryVariable]);
            Assert.Equal(TimeSpan.FromSeconds(45), request.Timeout);
            Assert.True(Directory.Exists(configDirectory));
            Assert.Equal(new AzureCliResult(0, "[]", string.Empty, false), result);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Uses_explicit_timeout_and_reports_timed_out_process()
    {
        var processRunner = new FakeProcessRunner().Returns(new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true));
        var runner = new AzureCliRunner(processRunner, Options.Create(new AzureCliOptions
        {
            ConfigDirectory = Path.Combine(Path.GetTempPath(), "AzureExploreAgent.Tests", "shared"),
            SubscriptionId = "sub",
        }));

        var result = await runner.RunAsync(["login"], TimeSpan.FromSeconds(90), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(90), processRunner.Requests.Single().Timeout);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public void Defaults_config_directory_to_an_isolated_temp_folder()
    {
        var options = new AzureCliOptions();

        Assert.Equal(Path.Combine(Path.GetTempPath(), "AzureExploreAgent", "azure-cli"), options.ResolveConfigDirectory());
    }
}
