using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Notifications.UnitTests;

internal sealed class TestEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;

    public string ApplicationName { get; set; } = "Notifications.UnitTests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
