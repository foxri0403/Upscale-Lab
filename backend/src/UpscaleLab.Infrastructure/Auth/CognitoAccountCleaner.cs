using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using UpscaleLab.Application.Auth;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class CognitoAccountCleaner(CognitoOptions options) : IUnverifiedAccountCleaner
{
    public async Task<bool> DeleteUnconfirmedAsync(string email, CancellationToken cancellationToken)
    {
        // Admin APIs require the deployment's IAM credentials, unlike public sign-up.
        using var client = new AmazonCognitoIdentityProviderClient(RegionEndpoint.GetBySystemName(options.Region));
        try
        {
            var account = await client.AdminGetUserAsync(new AdminGetUserRequest
            {
                UserPoolId = options.UserPoolId,
                Username = email
            }, cancellationToken);
            if (account.UserStatus != UserStatusType.UNCONFIRMED) return false;
            await client.AdminDeleteUserAsync(new AdminDeleteUserRequest
            {
                UserPoolId = options.UserPoolId,
                Username = email
            }, cancellationToken);
            return true;
        }
        catch (UserNotFoundException) { return true; }
    }
}
