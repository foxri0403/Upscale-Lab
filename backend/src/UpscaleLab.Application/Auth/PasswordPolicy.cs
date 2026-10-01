using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Application.Auth;

public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;
    public const string ErrorMessage =
        "비밀번호는 10~128자이며 대문자, 숫자, 특수문자를 각각 하나 이상 포함해야 합니다.";

    public static bool IsSatisfiedBy(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        var hasUppercase = false;
        var hasDigit = false;
        var hasSpecialCharacter = false;

        foreach (var character in password)
        {
            hasUppercase |= char.IsUpper(character);
            hasDigit |= char.IsDigit(character);
            hasSpecialCharacter |= !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character);
        }

        return hasUppercase && hasDigit && hasSpecialCharacter;
    }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    public PasswordPolicyAttribute()
    {
        ErrorMessage = PasswordPolicy.ErrorMessage;
    }

    public override bool IsValid(object? value) =>
        value is string password && PasswordPolicy.IsSatisfiedBy(password);
}
