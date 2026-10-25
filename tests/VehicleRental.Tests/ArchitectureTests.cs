using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Interfaces;
using Car_Rental_System_BE.Infrastructure.Repositories;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace VehicleRental.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Inner_layers_do_not_reference_outer_layers_or_persistence_drivers()
    {
        var domain = typeof(User).Assembly;
        var application = typeof(AccountService).Assembly;
        Assert.DoesNotContain(domain.GetReferencedAssemblies(), name =>
            name.Name!.StartsWith("VehicleRental.") || IsExternalDependency(name));
        Assert.DoesNotContain(application.GetReferencedAssemblies(), name =>
            name.Name is "VehicleRental.Infrastructure" or "VehicleRental.WebApi" || IsExternalDependency(name));
    }

    [Fact]
    public void Controllers_depend_on_services_instead_of_repository_or_infrastructure_types()
    {
        var controllers = typeof(AccountController).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type));
        foreach (var type in controllers)
        {
            var dependencies = type.GetConstructors().SelectMany(ctor => ctor.GetParameters());
            Assert.DoesNotContain(dependencies, parameter =>
                parameter.ParameterType.Namespace?.StartsWith("Car_Rental_System_BE.Application.Interfaces.Repositories") == true ||
                parameter.ParameterType.Namespace?.StartsWith("Car_Rental_System_BE.Domain.Interfaces") == true ||
                parameter.ParameterType.Namespace?.StartsWith("Car_Rental_System_BE.Infrastructure") == true);
        }
    }

    [Fact]
    public void Services_do_not_implement_repository_interfaces()
    {
        var repositories = typeof(IAccountRepository).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(IAccountRepository).Namespace)
            .Concat(typeof(IUserRepository).Assembly.GetTypes().Where(type =>
                type.IsInterface && type.Namespace == typeof(IUserRepository).Namespace)).ToHashSet();
        var services = typeof(AccountService).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AccountService).Namespace);
        foreach (var service in services)
            Assert.DoesNotContain(service.GetInterfaces(), repositories.Contains);
        Assert.Contains(typeof(IAccountRepository), typeof(NpgsqlAccountRepository).GetInterfaces());
    }

    private static bool IsExternalDependency(AssemblyName name) =>
        name.Name is "Npgsql" or "StackExchange.Redis" or "CloudinaryDotNet" ||
        name.Name!.StartsWith("Microsoft.AspNetCore") || name.Name.StartsWith("Microsoft.EntityFrameworkCore") || name.Name.StartsWith("Npgsql.EntityFrameworkCore");
}
