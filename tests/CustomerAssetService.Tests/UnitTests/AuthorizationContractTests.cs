using CustomerAssetService.Controllers;
using CustomerAssetService.Security;
using Microsoft.AspNetCore.Authorization;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class AuthorizationContractTests
{
    [Theory]
    [InlineData(typeof(CustomersController))]
    [InlineData(typeof(AssetsController))]
    public void ResourceControllers_RequireAllSupportedStaffRoles(Type controllerType)
    {
        var attribute = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(StaffRoles.All, attribute.Roles);
    }

    [Theory]
    [InlineData(typeof(CustomersController), "Create")]
    [InlineData(typeof(CustomersController), "Update")]
    [InlineData(typeof(CustomersController), "Deactivate")]
    [InlineData(typeof(AssetsController), "Create")]
    [InlineData(typeof(AssetsController), "Update")]
    [InlineData(typeof(AssetsController), "Deactivate")]
    public void Mutations_RequireAgentOrManager(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(StaffRoles.CustomerEditors, attribute.Roles);
    }
}
