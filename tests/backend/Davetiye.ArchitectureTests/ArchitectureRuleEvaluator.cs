using System.Reflection;
using System.Reflection.Emit;

namespace Davetiye.ArchitectureTests;

internal static class ArchitectureRuleEvaluator
{
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => unchecked((ushort)opCode.Value));

    public static IReadOnlyList<string> FindCrossModuleReferenceViolations(
        IEnumerable<Type> candidateTypes)
    {
        var violations = new List<string>();

        foreach (var sourceType in candidateTypes)
        {
            var sourceModule = GetModule(sourceType);
            if (sourceModule is null)
            {
                continue;
            }

            foreach (var referencedType in GetReferencedTypes(sourceType))
            {
                var referencedModule = GetModule(referencedType);
                if (referencedModule is null || referencedModule == sourceModule)
                {
                    continue;
                }

                if (IsIdentifier(referencedType) ||
                    IsSafeApplicationContract(referencedType) ||
                    IsAllowedSharedKernelReference(referencedType))
                {
                    continue;
                }

                violations.Add(
                    $"{sourceType.FullName} references foreign module type {referencedType.FullName}.");
            }
        }

        return [.. violations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> FindContractSurfaceViolations(
        IEnumerable<Type> candidateTypes)
    {
        var violations = new List<string>();

        foreach (var type in candidateTypes.Where(IsInApplicationContractsTree))
        {
            var typeName = type.Name;
            var namespaceName = type.Namespace ?? string.Empty;

            if (!ArchitecturePolicy.IsApplicationContractNamespace(namespaceName))
            {
                violations.Add($"Contract namespace is not a public contract surface: {type.FullName}.");
            }

            if (!type.IsVisible)
            {
                violations.Add($"Contract type must be public: {type.FullName}.");
            }

            if (!ArchitecturePolicy.IsAllowedContractTypeName(typeName))
            {
                violations.Add($"Contract type exposes an implementation/handler surface: {type.FullName}.");
            }
        }

        return [.. violations.Order(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> FindSharedKernelViolations(
        IEnumerable<Type> candidateTypes)
    {
        return
        [
            .. candidateTypes
            .Where(type => type.Namespace?.StartsWith(
                "Davetiye.Domain.Modules.SharedKernel",
                StringComparison.Ordinal) == true)
            .Where(type => !ArchitecturePolicy.IsAllowedSharedKernelTypeName(type.Name))
            .Select(type => $"SharedKernel type is not allowlisted: {type.FullName}.")
            .Order(StringComparer.Ordinal)
        ];
    }

    private static bool IsSafeApplicationContract(Type type) =>
        type.IsVisible &&
        ArchitecturePolicy.IsApplicationContractNamespace(type.Namespace ?? string.Empty) &&
        ArchitecturePolicy.IsAllowedContractTypeName(type.Name);

    private static bool IsIdentifier(Type type) =>
        type.Name.EndsWith("Id", StringComparison.Ordinal);

    /// <summary>
    /// SharedKernel exists precisely to be referenced across module boundaries for its allowlisted
    /// primitives (docs/PHASE_0_BASELINE.md §2: "id, clock, money ve result gibi küçük
    /// primitive'lerle sınırlıdır"), so a foreign module using e.g. <c>IClock</c> is not itself a
    /// cross-module violation - only referencing a non-allowlisted SharedKernel type from another
    /// module is (still caught by the general check below, and separately by
    /// <see cref="FindSharedKernelViolations"/> for anything non-allowlisted that lives there at all).
    /// </summary>
    private static bool IsAllowedSharedKernelReference(Type type) =>
        type.Namespace?.StartsWith("Davetiye.Domain.Modules.SharedKernel", StringComparison.Ordinal) == true &&
        ArchitecturePolicy.IsAllowedSharedKernelTypeName(type.Name);

    private static bool IsInApplicationContractsTree(Type type) =>
        type.Namespace?.StartsWith(
            "Davetiye.Application.Modules.",
            StringComparison.Ordinal) == true &&
        type.Namespace.Split('.').Contains("Contracts", StringComparer.Ordinal);

    private static string? GetModule(Type type)
    {
        var segments = type.Namespace?.Split('.', StringSplitOptions.RemoveEmptyEntries);

        return segments is { Length: >= 4 } &&
            segments[0] == "Davetiye" &&
            segments[2] == "Modules"
                ? segments[3]
                : null;
    }

    private static IEnumerable<Type> GetReferencedTypes(Type sourceType)
    {
        const BindingFlags declaredMembers =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        var referencedTypes = new List<Type>();

        if (sourceType.BaseType is not null)
        {
            referencedTypes.Add(sourceType.BaseType);
        }

        referencedTypes.AddRange(sourceType.GetInterfaces());
        referencedTypes.AddRange(sourceType.GetFields(declaredMembers).Select(field => field.FieldType));
        referencedTypes.AddRange(sourceType.GetProperties(declaredMembers).Select(property => property.PropertyType));
        referencedTypes.AddRange(sourceType.GetEvents(declaredMembers).Select(@event => @event.EventHandlerType!));
        referencedTypes.AddRange(sourceType.GetMethods(declaredMembers).Select(method => method.ReturnType));
        referencedTypes.AddRange(sourceType.GetMethods(declaredMembers)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType));
        referencedTypes.AddRange(sourceType.GetConstructors(declaredMembers)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType));
        referencedTypes.AddRange(GetGenericConstraintTypes(sourceType.GetGenericArguments()));
        referencedTypes.AddRange(GetGenericParameterAttributeTypes(sourceType.GetGenericArguments()));
        referencedTypes.AddRange(GetAttributeTypes(sourceType.GetCustomAttributesData()));

        var declaredMemberInfos = sourceType.GetMembers(declaredMembers);
        referencedTypes.AddRange(declaredMemberInfos
            .SelectMany(member => GetAttributeTypes(member.GetCustomAttributesData())));

        var declaredMethods = sourceType.GetMethods(declaredMembers);
        referencedTypes.AddRange(declaredMethods
            .SelectMany(method => GetGenericConstraintTypes(method.GetGenericArguments())));
        referencedTypes.AddRange(declaredMethods
            .SelectMany(method => GetGenericParameterAttributeTypes(method.GetGenericArguments())));
        referencedTypes.AddRange(declaredMethods
            .SelectMany(method => method.GetParameters())
            .SelectMany(parameter => GetAttributeTypes(parameter.GetCustomAttributesData())));
        referencedTypes.AddRange(declaredMethods
            .SelectMany(method => GetAttributeTypes(method.ReturnParameter.GetCustomAttributesData())));
        referencedTypes.AddRange(sourceType.GetConstructors(declaredMembers)
            .SelectMany(constructor => GetAttributeTypes(constructor.GetCustomAttributesData())));

        var executableMembers = sourceType
            .GetMethods(declaredMembers)
            .Cast<MethodBase>()
            .Concat(sourceType.GetConstructors(declaredMembers));
        referencedTypes.AddRange(executableMembers.SelectMany(GetIlReferencedTypes));

        return referencedTypes
            .Where(type => type is not null)
            .SelectMany(FlattenType)
            .Where(type => type != sourceType && !type.IsGenericParameter && type != typeof(void));
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nestedType in FlattenType(elementType))
            {
                yield return nestedType;
            }
        }

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var nestedType in FlattenType(genericArgument))
            {
                yield return nestedType;
            }
        }
    }

    private static IEnumerable<Type> GetGenericConstraintTypes(IEnumerable<Type> genericArguments) =>
        genericArguments
            .Where(argument => argument.IsGenericParameter)
            .SelectMany(argument => argument.GetGenericParameterConstraints());

    private static IEnumerable<Type> GetGenericParameterAttributeTypes(
        IEnumerable<Type> genericArguments) =>
        genericArguments
            .Where(argument => argument.IsGenericParameter)
            .SelectMany(argument => GetAttributeTypes(argument.GetCustomAttributesData()));

    private static IEnumerable<Type> GetAttributeTypes(
        IEnumerable<CustomAttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            yield return attribute.AttributeType;

            foreach (var argument in attribute.ConstructorArguments)
            {
                foreach (var argumentType in GetAttributeArgumentTypes(argument))
                {
                    yield return argumentType;
                }
            }

            foreach (var argument in attribute.NamedArguments)
            {
                foreach (var argumentType in GetAttributeArgumentTypes(argument.TypedValue))
                {
                    yield return argumentType;
                }
            }
        }
    }

    private static IEnumerable<Type> GetAttributeArgumentTypes(
        CustomAttributeTypedArgument argument)
    {
        yield return argument.ArgumentType;

        if (argument.Value is Type typeValue)
        {
            yield return typeValue;
        }

        if (argument.Value is not IReadOnlyCollection<CustomAttributeTypedArgument> values)
        {
            yield break;
        }

        foreach (var value in values)
        {
            foreach (var nestedType in GetAttributeArgumentTypes(value))
            {
                yield return nestedType;
            }
        }
    }

    private static IEnumerable<Type> GetIlReferencedTypes(MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();

        if (il is null)
        {
            yield break;
        }

        var position = 0;
        while (position < il.Length)
        {
            var opCodeValue = ReadOpCodeValue(il, ref position);
            if (!OpCodesByValue.TryGetValue(opCodeValue, out var opCode))
            {
                throw new InvalidOperationException(
                    $"Unknown IL opcode 0x{opCodeValue:X4} in {method.DeclaringType?.FullName}.{method.Name}.");
            }

            if (opCode.OperandType is OperandType.InlineField or
                OperandType.InlineMethod or
                OperandType.InlineTok or
                OperandType.InlineType)
            {
                var metadataToken = BitConverter.ToInt32(il, position);
                position += sizeof(int);

                foreach (var referencedType in ResolveTokenTypes(method, metadataToken))
                {
                    yield return referencedType;
                }

                continue;
            }

            position += GetOperandSize(opCode.OperandType, il, position);
        }
    }

    private static ushort ReadOpCodeValue(byte[] il, ref int position)
    {
        var firstByte = il[position++];
        if (firstByte != 0xFE)
        {
            return firstByte;
        }

        return (ushort)(0xFE00 | il[position++]);
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int position) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or
            OperandType.ShortInlineI or
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or
            OperandType.InlineI or
            OperandType.InlineField or
            OperandType.InlineMethod or
            OperandType.InlineSig or
            OperandType.InlineString or
            OperandType.InlineTok or
            OperandType.InlineType or
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => sizeof(int) +
                (BitConverter.ToInt32(il, position) * sizeof(int)),
            _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}.")
        };

    private static IEnumerable<Type> ResolveTokenTypes(
        MethodBase method,
        int metadataToken)
    {
        MemberInfo? referencedMember;

        try
        {
            referencedMember = method.Module.ResolveMember(
                metadataToken,
                method.DeclaringType?.GetGenericArguments(),
                method.IsGenericMethod ? method.GetGenericArguments() : null);
        }
        catch (ArgumentException)
        {
            yield break;
        }

        switch (referencedMember)
        {
            case Type referencedType:
                yield return referencedType;
                break;
            case FieldInfo field:
                if (field.DeclaringType is not null)
                {
                    yield return field.DeclaringType;
                }

                yield return field.FieldType;
                break;
            case MethodInfo referencedMethod:
                if (referencedMethod.DeclaringType is not null)
                {
                    yield return referencedMethod.DeclaringType;
                }

                yield return referencedMethod.ReturnType;
                foreach (var parameter in referencedMethod.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                foreach (var genericArgument in referencedMethod.GetGenericArguments())
                {
                    yield return genericArgument;
                }

                break;
            case ConstructorInfo constructor:
                if (constructor.DeclaringType is not null)
                {
                    yield return constructor.DeclaringType;
                }

                foreach (var parameter in constructor.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
        }
    }
}
