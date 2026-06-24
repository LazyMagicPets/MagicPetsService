# LocalWebApi

This project is useful for debugging Controllers and Repository Models. 

The idea is to launch two copies of Visual Studio, one opened on the client solution and the other on this service solution. 
You can then make calls, from the client solution, against this service solution and debug in both solutions. 

## Prerequisites
This service uses the AWS SDK to access AWS resources. You must have an AWS account and have the AWS CLI installed and
you must have deployed the sam.devenv.yaml stack to your AWS account. See the README notes in the AWSTemplates folder for more information.

## AWS Profile & Access 
It is necessary to create aws profiles to allow this service to use AWS resources and set environmental variables for use by the service. This is done in the LaunchSettings.json file.

For example:
```
{
  "profiles": {
    "lzm-dev-uptown": {
      "commandName": "Project",
      "launchBrowser": false,
      "applicationUrl": "https://localhost:5001;http://localhost:5000;",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "AWSPROFILE": "lzm-dev",
        "AWSREGION": "us-west-2",
        "TenantKey": "uptown",
        "UserPoolName": "EmployeeUserPool",
        "UserPoolClientId": "4nb1k008datg4e0uir6279cbqn",
        "UserPoolId": "us-west-2_hbLYCDmAh",
        "IdentityPoolId": "",
        "UserPoolSecurityLevel": "1"
      }
    },
    ...
}
```

The environmentalVariable section is where you set the variables necessary to configure your LocalWebAPI to service local clients. All of these, except ASPNETCORE_ENVIRONMENT, are updated from the outputs of the sam.devenv.yaml stack. These outputs are stored in AWSTemplates/outputs.json.

You create multiple profiles, one for each type of client you want to test. Fro example, use the lzm-dev-uptown profile to test the Employee client against the uptown tenancy. 

Notes:
- TenantKey variable is used to select the correct DynamoDB table for the tenancy. 
- In this example, the Employee Client will receive the user pool information for the EmployeeUserPool. 

### Why not use subdomains like the CloudFront service?
Setting up subdomains is a good idea, but it is a pain to set up and maintain due to the need to set up SSL certificates for the Kestrel server. Even though you can setup up self-signed certificates, it is still a pain to setup. 

Note that you can still debug your client using the CloudFront service, so the only time this is an issue is when you want to debug the controllers locally. 

If you want to use subdomains, you can set up the Kestrel server to use them.

## Limitations

This service provides functionality similar to, but not identical to, calling Lambdas through an API Gateway. Remember that the AWS Lambda service can launch multiple lambda instances to handle high volumes of calls. This means that you may have more than one lambda accessing AWS Resources (like the DynamoDB service) at the same time. It is not possible to test the resulting resource contention in this local service. 

