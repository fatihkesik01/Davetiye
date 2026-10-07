namespace Davetiye.Application.Modules.Templates.Contracts;

/// <summary>Template-owned read boundary for feature-module support checks.</summary>
public interface ITemplateModuleSupportReader
{
    Task<bool> SupportsModuleAsync(string templateKey, string moduleKey, CancellationToken cancellationToken);
}
