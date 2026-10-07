using Davetiye.Domain.Modules.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Templates;

internal sealed class TemplateDefinitionConfiguration : IEntityTypeConfiguration<TemplateDefinition>
{
    public void Configure(EntityTypeBuilder<TemplateDefinition> builder)
    {
        builder.HasKey(template => template.Id);

        builder.Property(template => template.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(template => template.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(template => template.Description)
            .HasMaxLength(TemplateDefinition.DescriptionMaxLength);

        builder.Property(template => template.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(template => template.PreviewImageUrl)
            .HasMaxLength(500);

        builder.Property(template => template.SupportedModules)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(template => template.RequiredFields)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(template => template.RecommendedFields)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(template => template.Key)
            .IsUnique();

        // Supports the public /sablonlar catalog listing's "active templates only" query (M6).
        builder.HasIndex(template => template.IsActive);
    }
}
