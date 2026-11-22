using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace StS2AP.Loader;

/// <summary>
/// Resolves every type and member a client build references in the game and its libraries, so
/// a build compiled against another game version fails at startup instead of when a missing
/// member is first used, possibly mid-run. It finds removed or changed APIs, not changed game
/// behaviour.
/// </summary>
/// <remarks>
/// Forcing each method to compile is not enough: the JIT defers a missing call inside a
/// try block, which every async method body is, until the call runs. Resolving the assembly's
/// type and member references covers every call site at once.
/// </remarks>
internal static class VariantReferenceCheck
{
    private const BindingFlags AnyMember =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static IReadOnlyList<string> FindMissingReferences(Assembly assembly)
    {
        var problems = new List<string>();
        try
        {
            assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            problems.AddRange(ex.LoaderExceptions.OfType<Exception>().Select(exception => exception.Message));
        }

        Module module = assembly.ManifestModule;
        using var stream = File.OpenRead(assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();

        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            try
            {
                module.ResolveType(MetadataTokens.GetToken(handle));
            }
            catch (Exception ex)
            {
                problems.Add($"{DescribeType(metadata, handle)}: {ex.Message}");
            }
        }

        foreach (MemberReferenceHandle handle in metadata.MemberReferences)
        {
            MemberReference reference = metadata.GetMemberReference(handle);
            string name = metadata.GetString(reference.Name);
            try
            {
                module.ResolveMember(MetadataTokens.GetToken(handle));
            }
            catch (ArgumentException ex) when (ex.InnerException is BadImageFormatException)
            {
                // A member of a generic type instantiated with this build's own type parameters
                // cannot be resolved without a generic context; check the generic definition.
                string? problem = CheckGenericMember(module, metadata, reference.Parent, name);
                if (problem != null)
                    problems.Add(problem);
            }
            catch (Exception ex)
            {
                problems.Add($"{DescribeParent(metadata, reference.Parent)}.{name}: {ex.Message}");
            }
        }
        return problems.Distinct().ToList();
    }

    private static string? CheckGenericMember(Module module, MetadataReader metadata, EntityHandle parent, string name)
    {
        if (parent.Kind != HandleKind.TypeSpecification)
            return null;
        BlobReader signature = metadata.GetBlobReader(
            metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
        if (signature.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
            return null;
        signature.ReadSignatureTypeCode(); // class or value type
        EntityHandle definition = signature.ReadTypeHandle();
        if (definition.Kind != HandleKind.TypeReference)
            return null; // one of this build's own generic types

        string typeName = DescribeType(metadata, (TypeReferenceHandle)definition);
        try
        {
            Type type = module.ResolveType(MetadataTokens.GetToken(definition));
            bool found = name == ".ctor"
                ? type.GetConstructors(AnyMember).Length > 0
                : type.GetMember(name, AnyMember).Length > 0;
            return found ? null : $"{typeName}.{name}: member not found";
        }
        catch (Exception ex)
        {
            return $"{typeName}: {ex.Message}";
        }
    }

    private static string DescribeParent(MetadataReader metadata, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeReference => DescribeType(metadata, (TypeReferenceHandle)parent),
        HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
        _ => "<generic type>",
    };

    private static string DescribeType(MetadataReader metadata, TypeReferenceHandle handle)
    {
        TypeReference type = metadata.GetTypeReference(handle);
        string name = metadata.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return $"{DescribeType(metadata, (TypeReferenceHandle)type.ResolutionScope)}+{name}";
        string ns = metadata.GetString(type.Namespace);
        return ns.Length > 0 ? $"{ns}.{name}" : name;
    }
}
