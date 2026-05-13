using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Hookbin.API.Controllers;

namespace Hookbin.UnitTests.API.Controllers;

public sealed class InfoControllerTests
{
    private static InfoController CreateController()
    {
        var controller = new InfoController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public void GetVersion_ReturnsOk_WithVersionObject()
    {
        var result = CreateController().GetVersion();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetVersion_ReturnsAssemblyVersion_WhenPresent()
    {
        var result = CreateController().GetVersion();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var versionProperty = ok.Value!.GetType().GetProperty("version", BindingFlags.Instance | BindingFlags.Public);
        versionProperty.Should().NotBeNull("the anonymous object must expose a 'version' property");
        var versionString = versionProperty!.GetValue(ok.Value) as string;
        versionString.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetVersion_ReturnsDotSeparatedSemverLikeFormat()
    {
        var result = CreateController().GetVersion();

        var ok = (OkObjectResult)result;
        var versionString = (string)ok.Value!.GetType()
            .GetProperty("version", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(ok.Value)!;

        versionString.Split('.').Should().HaveCountGreaterThanOrEqualTo(2,
            "version should be in major.minor[.patch] form");
    }

    [Fact]
    public void GetVersion_NeverReturnsNullString()
    {
        var result = CreateController().GetVersion();

        var ok = (OkObjectResult)result;
        var versionString = (string)ok.Value!.GetType()
            .GetProperty("version", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(ok.Value)!;

        versionString.Should().NotBeNull();
    }
}
