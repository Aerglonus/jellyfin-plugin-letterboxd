using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LetterboxdSync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LetterboxdSync.Tests;

public class ServiceRegistratorTests
{
    [Fact]
    public void RegisterServices_AddsPlaybackHandlerAsHostedService()
    {
        var services = new ServiceCollection();
        var registrator = new ServiceRegistrator();

        registrator.RegisterServices(services, null!);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            descriptor.ImplementationType == typeof(PlaybackHandler));

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            descriptor.ImplementationType == typeof(InjectionService));
    }
}

