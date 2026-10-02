using System.Reflection;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Mvc;

namespace AdminSchemaRepo;

public partial class TenantUserRepo
{
    // The TenantDB level comes from x-lz-tablelevel in openapi.admin-schema.yaml.
    protected override void ConstructorExtensions()
    {
        debug = false; // Log all calls to the console
        base.ConstructorExtensions();
    }

    private AmazonCognitoIdentityProviderClient _cognitoClient;

    private async Task<bool> CreateCognitoUser(string userPoolId, string userName, string email)
    {
        try
        {
            var signUpRequest = new AdminCreateUserRequest
            {
                UserPoolId = userPoolId,
                Username = userName,
                UserAttributes = new List<AttributeType>
                {
                    new AttributeType { Name = "email", Value = email },
                    new AttributeType { Name = "email_verified", Value = "true" }
                }
            };
            Console.WriteLine("Calling AdminCreateUserAsync");
            await _cognitoClient.AdminCreateUserAsync(signUpRequest);
            Console.WriteLine("AdminCreateUserAsync completed");
            return true; // Success case, will be handled by base.CreateAsync
        }
        catch
        {
            Console.WriteLine("Error in CreateCognitoUser");
            return false;
        }
    }

    private async Task<bool> UserExistsInCognito(string userPoolId, string userName)
    {
        try
        {
            var getUserRequest = new AdminGetUserRequest
            {
                UserPoolId = userPoolId,
                Username = userName
            };
            Console.WriteLine("Calling AdminGetUserAsync");
            await _cognitoClient.AdminGetUserAsync(getUserRequest);
            Console.WriteLine("AdminGetUserAsync completed");
            return true;
        }
        catch (UserNotFoundException)
        {
            Console.WriteLine("User not found in Cognito");
            return false;
        }
        catch (Exception)
        {
            Console.WriteLine("Error in UserExistsInCognito");
            throw;
        }
    }

    public override async Task<ActionResult<TenantUser>> CreateAsync(ICallerInfo callerInfo, TenantUser data)
    {
        // Validation
        if (string.IsNullOrEmpty(data.Login))
        {
            return new ObjectResult($"Login field empty.")
            {
                StatusCode = 400
            };
        }

        if (string.IsNullOrEmpty(data.Email))
        {
            return new ObjectResult($"Email field empty.")
            {
                StatusCode = 400
            };
        }

        try
        {
            // Setup Cognito client
            var awsRegionString = callerInfo.Headers["lz-cognito-region"];
            var userPoolId = callerInfo.Headers["lz-cognito-userpool-id"];

            if (string.IsNullOrEmpty(awsRegionString))
            {
                return new ObjectResult("Missing required header: LZ - Cognito - Region")
                {
                    StatusCode = 500
                };
            }

            if (string.IsNullOrEmpty(userPoolId))
            {
                return new BadRequestObjectResult("Missing required header: lz-cognito-userpool-id");
            }

            var awsRegion = Amazon.RegionEndpoint.GetBySystemName(awsRegionString);
            if (awsRegion == null)
            {
                return new ObjectResult($"Invalid AWS region specified: {awsRegionString}")
                {
                    StatusCode = 500
                };
            }

            Console.WriteLine($"Creating Cognito client in region {awsRegion.SystemName}");
            _cognitoClient = new AmazonCognitoIdentityProviderClient(awsRegion);
            Console.WriteLine("Cognito client created");

            // Check if user exists and create if not
            if (!await UserExistsInCognito(userPoolId, data.Login))
            {
                if (!await CreateCognitoUser(userPoolId, data.Login, data.Email)) // Error case
                {
                    throw new Exception("Error creating user in Cognito");
                }
            }

            // Create user in base system
            Console.WriteLine("Creating user record in TenantDB");
            var result = await base.CreateAsync(callerInfo, data);
            if(result.Value == null)
            {
                throw new Exception("Error creating user record in TenantDB");
            }
            Console.WriteLine("User record created in TenantDB");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in CreateAsync: {ex.Message}");
            return new ObjectResult($"Internal server error: {ex.Message}")
            {
                StatusCode = 500
            };
        }
    }

    public override async Task<StatusCodeResult> DeleteAsync(ICallerInfo callerInfo, string id)
    {
        // TODO: Implement Cognito user deletion
        return await base.DeleteAsync(callerInfo, id);
    }
}