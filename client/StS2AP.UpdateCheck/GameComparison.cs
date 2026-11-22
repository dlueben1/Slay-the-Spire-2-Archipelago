using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace StS2AP.UpdateCheck;

/// <summary>
/// Compares two game versions where it matters to the client: the methods it patches, and which game
/// content exists. A patch can attach to a method whose behaviour has changed, so these need a person
/// to read them. The comparison uses decompiled text because metadata tokens make raw IL differ between
/// builds, and because an async method's body lives in a compiler-generated class.
/// </summary>
internal static partial class GameComparison
{
    internal sealed record PatchedMethod(string Name, string Status, string? Previous, string? Current);

    internal sealed record ContentChange(string Category, List<string> Added, List<string> Removed);

    internal sealed record Result(List<PatchedMethod> PatchedMethods, List<ContentChange> ContentChanges);

    private const string AbstractModelName = "MegaCrit.Sts2.Core.Models.AbstractModel";

    internal static Result Run(
        IEnumerable<int> patchedMethodTokens,
        string gameDirectory,
        string previousGameDirectory,
        string gameDataDirectory)
    {
        var current = new GameAssembly(gameDirectory, gameDataDirectory);
        var previous = new GameAssembly(previousGameDirectory, gameDataDirectory);

        var methods = new Dictionary<string, PatchedMethod>();
        foreach (int token in patchedMethodTokens)
        {
            GameMethod target = current.Resolve(token);
            if (!methods.ContainsKey(target.DisplayName))
                methods[target.DisplayName] = Compare(target, current, previous);
        }

        return new Result(
            methods.Values.OrderBy(method => method.Name, StringComparer.Ordinal).ToList(),
            CompareContent(current, previous));
    }

    private static PatchedMethod Compare(GameMethod target, GameAssembly current, GameAssembly previous)
    {
        string currentText = current.Decompile(target);
        string? previousText = previous.DecompileMatching(target);
        if (previousText == null)
            return new PatchedMethod(target.DisplayName, "new", null, currentText);
        return previousText == currentText
            ? new PatchedMethod(target.DisplayName, "same", null, null)
            : new PatchedMethod(target.DisplayName, "changed", previousText, currentText);
    }

    private static List<ContentChange> CompareContent(GameAssembly current, GameAssembly previous)
    {
        Dictionary<string, string> currentModels = current.ModelCategories();
        Dictionary<string, string> previousModels = previous.ModelCategories();
        return currentModels.Values.Concat(previousModels.Values)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Select(category => new ContentChange(
                category,
                Missing(currentModels, previousModels, category),
                Missing(previousModels, currentModels, category)))
            .Where(change => change.Added.Count > 0 || change.Removed.Count > 0)
            .ToList();
    }

    private static List<string> Missing(
        Dictionary<string, string> present,
        Dictionary<string, string> other,
        string category) =>
        present
            .Where(model => model.Value == category && !other.ContainsKey(model.Key))
            .Select(model => model.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>A patched method, or the method that owns a patched state machine.</summary>
    private sealed record GameMethod(ITypeDefinition Type, string MethodName, string? Signature, IMethod? Method)
    {
        internal string DisplayName => Signature == null
            ? $"{Type.ReflectionName}.{MethodName}"
            : $"{Type.ReflectionName}.{Signature}";
    }

    private sealed class GameAssembly
    {
        private readonly CSharpDecompiler _decompiler;

        internal GameAssembly(string directory, string gameDataDirectory)
        {
            string path = Path.Combine(directory, "sts2.dll");
            var resolver = new UniversalAssemblyResolver(path, false, ".NETCoreApp,Version=v9.0");
            resolver.AddSearchDirectory(directory);
            resolver.AddSearchDirectory(gameDataDirectory);
            // The installed game has an sts2.xml beside the DLL, and its doc comments aren't code changes.
            var settings = new DecompilerSettings { ShowXmlDocumentation = false };
            _decompiler = new CSharpDecompiler(path, resolver, settings);
        }

        internal GameMethod Resolve(int metadataToken)
        {
            var method = (IMethod)_decompiler.TypeSystem.MainModule.ResolveEntity(
                MetadataTokens.EntityHandle(metadataToken));
            ITypeDefinition type = method.DeclaringTypeDefinition!;

            // Harmony sees an async or iterator body as MoveNext on a generated class whose number
            // changes between builds (<OnPlay>d__12), so compare the method that owns it.
            Match stateMachine = StateMachineName().Match(type.Name);
            return stateMachine.Success && type.DeclaringTypeDefinition is { } owner
                ? new GameMethod(owner, stateMachine.Groups["method"].Value, null, null)
                : new GameMethod(type, method.Name, SignatureOf(method), method);
        }

        internal string Decompile(GameMethod target) =>
            target.Method != null
                ? Decompile(target.Method)
                : DecompileAll(MethodsOf(target.Type).Where(method => method.Name == target.MethodName));

        /// <summary>
        /// The same method in this version, or every overload of that name when its parameters
        /// changed, so the change shows up in the report. Null when nothing has that name.
        /// </summary>
        internal string? DecompileMatching(GameMethod target)
        {
            ITypeDefinition? type = _decompiler.TypeSystem
                .FindType(new FullTypeName(target.Type.ReflectionName))
                .GetDefinition();
            if (type == null)
                return null;

            IMethod[] named = MethodsOf(type).Where(method => method.Name == target.MethodName).ToArray();
            if (named.Length == 0)
                return null;
            IMethod? exact = named.FirstOrDefault(method => SignatureOf(method) == target.Signature);
            return exact != null ? Decompile(exact) : DecompileAll(named);
        }

        /// <summary>Concrete game models by class name, with the kind of model each one is.</summary>
        internal Dictionary<string, string> ModelCategories() =>
            _decompiler.TypeSystem.MainModule.TypeDefinitions
                .Where(type => !type.IsAbstract)
                .Select(type => (type.ReflectionName, Category: CategoryOf(type)))
                .Where(model => model.Category != null)
                .ToDictionary(model => model.ReflectionName, model => model.Category!);

        private static string? CategoryOf(ITypeDefinition type)
        {
            ITypeDefinition current = type;
            while (current.DirectBaseTypes
                       .FirstOrDefault(baseType => baseType.Kind == TypeKind.Class)
                       ?.GetDefinition() is { } baseType)
            {
                if (baseType.FullName == AbstractModelName)
                    return current == type ? null : current.Name;
                current = baseType;
            }
            return null;
        }

        private string Decompile(IMethod method) =>
            _decompiler.DecompileAsString(((IEntity?)method.AccessorOwner ?? method).MetadataToken);

        private string DecompileAll(IEnumerable<IMethod> methods) =>
            string.Join("\n", methods.Select(Decompile).Distinct());

        private static IEnumerable<IMethod> MethodsOf(ITypeDefinition type) =>
            type.Methods.Concat(type.GetAccessors(options: GetMemberOptions.IgnoreInheritedMembers));

        private static string SignatureOf(IMethod method) =>
            $"{method.Name}({string.Join(", ", method.Parameters.Select(parameter => parameter.Type.ReflectionName))})";
    }

    [GeneratedRegex(@"^<(?<method>[^>]+)>d__\d+$")]
    private static partial Regex StateMachineName();
}
