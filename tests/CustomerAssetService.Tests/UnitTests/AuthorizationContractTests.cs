using CustomerAssetService.Controllers;
using CustomerAssetService.Security;
using Microsoft.AspNetCore.Authorization;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class AuthorizationContractTests
{
    [Theory]
    [InlineData(typeof(CustomersController), "GetAll")]
    [InlineData(typeof(CustomersController), "GetAssets")]
    public void CollectionReads_RequireAllSupportedStaffRoles(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(StaffRoles.All, attribute.Roles);
    }

    [Theory]
    [InlineData(typeof(CustomersController))]
    [InlineData(typeof(AssetsController))]
    public void IndividualReads_AllowTheInternalJobServiceScheme(Type controllerType)
    {
        var method = controllerType.GetMethod("GetById")!;
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Contains(InternalServiceAuthenticationDefaults.Scheme, attribute.AuthenticationSchemes);
        Assert.Contains(StaffRoles.InternalService, attribute.Roles);
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
