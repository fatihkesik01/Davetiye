#pragma warning disable IDE0130 // Deliberately production-shaped namespaces used by architecture tests.
#pragma warning disable IDE0290 // Explicit constructor is required for constructor-attribute coverage.

namespace Davetiye.Domain.Modules.OwningModule
{
    [AttributeUsage(
        AttributeTargets.Class |
        AttributeTargets.Constructor |
        AttributeTargets.GenericParameter)]
    public sealed class OwnedMarkerAttribute : Attribute
    {
    }

    public sealed class OwnedEntity
    {
    }

    public readonly record struct OwnedEntityId(Guid Value);

    public interface IOwnedConstraint
    {
    }

    public static class OwnedOperations
    {
        public static void Touch()
        {
        }
    }
}

namespace Davetiye.Domain.Modules.ForeignModule
{
    public interface IEntityConfiguration<T>
    {
    }

    public sealed class InvalidEntityReference(
        Davetiye.Domain.Modules.OwningModule.OwnedEntity entity)
    {
        public Davetiye.Domain.Modules.OwningModule.OwnedEntity Entity { get; } = entity;
    }

    public sealed class AllowedIdentifierReference(
        Davetiye.Domain.Modules.OwningModule.OwnedEntityId entityId)
    {
        public Davetiye.Domain.Modules.OwningModule.OwnedEntityId EntityId { get; } = entityId;
    }

    public sealed class InvalidOwnedEntityConfiguration :
        IEntityConfiguration<Davetiye.Domain.Modules.OwningModule.OwnedEntity>
    {
    }

    public sealed class InvalidBodyOnlyReference
    {
        public static void Execute()
        {
            Davetiye.Domain.Modules.OwningModule.OwnedOperations.Touch();
        }
    }

    [Davetiye.Domain.Modules.OwningModule.OwnedMarker]
    public sealed class InvalidAttributeReference
    {
    }

    public sealed class InvalidGenericConstraint<T>
        where T : Davetiye.Domain.Modules.OwningModule.IOwnedConstraint
    {
    }

    public static class GenericInvoker
    {
        public static void Invoke<T>()
        {
        }
    }

    public sealed class InvalidMethodSpecReference
    {
        public static void Execute()
        {
            GenericInvoker.Invoke<Davetiye.Domain.Modules.OwningModule.OwnedEntity>();
        }
    }

    public sealed class InvalidConstructorAttributeReference
    {
        [Davetiye.Domain.Modules.OwningModule.OwnedMarker]
        public InvalidConstructorAttributeReference()
        {
        }
    }

    public sealed class InvalidTypeGenericParameterAttribute<
        [Davetiye.Domain.Modules.OwningModule.OwnedMarker] T>
    {
    }

    public sealed class InvalidMethodGenericParameterAttribute
    {
        public static void Execute<
            [Davetiye.Domain.Modules.OwningModule.OwnedMarker] T>()
        {
        }
    }
}

namespace Davetiye.Domain.Modules.SharedKernel
{
    internal sealed class InvitationRepository
    {
    }
}

namespace Davetiye.Application.Modules.OwningModule.Contracts
{
    public interface IAllowedContract
    {
    }

    internal interface IInternalContract
    {
    }

    public sealed class PaymentHandler
    {
    }

    public static class AllowedContractOperations
    {
        public static void Touch()
        {
        }
    }
}

namespace Davetiye.Application.Modules.OwningModule.Contracts.Internal.Commands
{
    public sealed class HiddenCommand
    {
    }
}

namespace Davetiye.Application.Modules.ForeignModule
{
    public sealed class AllowedContractReference(
        Davetiye.Application.Modules.OwningModule.Contracts.IAllowedContract contract)
    {
        public Davetiye.Application.Modules.OwningModule.Contracts.IAllowedContract Contract { get; } = contract;
    }

    public sealed class AllowedContractBodyReference
    {
        public static void Execute()
        {
            Davetiye.Application.Modules.OwningModule.Contracts.AllowedContractOperations.Touch();
        }
    }
}

#pragma warning restore IDE0130
#pragma warning restore IDE0290
