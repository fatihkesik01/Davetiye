namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>Generates a new 256-bit, lowercase-hex public locator. It never derives from IDs.</summary>
public interface IPublicCodeGenerator
{
    string Generate();
}
