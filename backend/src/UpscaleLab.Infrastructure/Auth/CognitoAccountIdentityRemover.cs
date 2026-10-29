using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class CognitoAccountIdentityRemover(CognitoOptions options) : IAccountIdentityRemover
{
    public async Task<string?> GetSubjectAsync(string email, CancellationToken ct)
    {
        using var client = new AmazonCognitoIdentityProviderClient(RegionEndpoint.GetBySystemName(options.Region));
        try
        {
            var user = await client.AdminGetUserAsync(new AdminGetUserRequest { UserPoolId = options.UserPoolId, Username = email }, ct);
            return user.UserAttributes?.FirstOrDefault(x => x.Name == "sub")?.Value
                ?? throw new ServiceUnavailableException("계정 정보를 확인할 수 없습니다. 잠시 후 다시 시도해 주세요.");
        }
        catch (UserNotFoundException) { return null; }
    }

    public async Task DeleteAsync(string subject, CancellationToken ct)
    {
        using var client = new AmazonCognitoIdentityProviderClient(RegionEndpoint.GetBySystemName(options.Region));
        try
        {
            // Immutable sub protects a newly registered account that reuses the email.
            await client.AdminDeleteUserAsync(new AdminDeleteUserRequest { UserPoolId = options.UserPoolId, Username = subject }, ct);
        }
        catch (UserNotFoundException) { }
    }
}
