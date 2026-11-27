using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SeaPowerKeel;

/// <summary>
/// Checks a mod DLL against the game that is running, before it is loaded: every type, method and field it uses from the
/// game's own assemblies must exist in this build. The same check as the workspace's compat-check tool, done in the game.
/// <para>Two kinds of findings. A class-load problem (a type of the mod derives from, implements or has a field of a
/// type this build lacks) breaks more than the mod: Unity's ECS start-up walks every type of every loaded assembly, throws
/// on such a type, and then no mission loads at all. Such a DLL is never loaded. A missing method or field use only
/// fails when that code runs, so the mod may well work (it can check the build itself first); that is a warning.</para>
/// </summary>
internal sealed class Preflight : IDisposable
{
    public sealed class Result
    {
        public readonly List<string> ClassLoad = new();
        public readonly List<string> Uses = new();
        /// <summary>Types and methods named by string (Harmony patch targets, AccessTools and reflection lookups) that this build lacks.</summary>
        public readonly List<string> Lookups = new();
        /// <summary>Harmony patches whose parameters do not fit the game method: they read or write the wrong kind of memory, which can take the game down.</summary>
        public readonly List<string> Fatal = new();
        public bool PatchCheckIncomplete;
        public readonly HashSet<string> PatchTargets = new(StringComparer.Ordinal);
        public readonly HashSet<string> LookupTargets = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Older packages ship two builds of one source for the two map kinds, side by side: <c>X.dll</c> (globe) and <c>XFlat.dll</c>
    /// (the old flat map). The one for the other map lacks things by design and stands down at its entry gate. When this DLL has
    /// such a partner in the same folder and the partner lacks nothing here, the partner's name is returned (else null).
    /// </summary>
    internal static string MatchingMapBuild(string dll, Result r, IReadOnlyDictionary<string, Result> results)
    {
        if (r.Uses.Count == 0)
            return null;
        string dir = Path.GetDirectoryName(dll) ?? "", name = Path.GetFileNameWithoutExtension(dll);
        string partner = name.EndsWith("Flat", StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - 4)
            : name + "Flat";
        if (partner.Length == 0)
            return null;
        string path = Path.Combine(dir, partner + ".dll");
        foreach (KeyValuePair<string, Result> kv in results)
        {
            if (string.Equals(Path.GetFullPath(kv.Key), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                return kv.Value.Uses.Count == 0 && kv.Value.ClassLoad.Count == 0 && kv.Value.Fatal.Count == 0 ? partner + ".dll" : null;
        }
        return null;
    }

    /// <summary>
    /// What the check of a plugin's DLL means for it, in the game and in the offline check alike: kept out (and why), or started
    /// with its warnings. <paramref name="matching"/>: see <see cref="MatchingMapBuild"/>; <paramref name="declared"/>: what its
    /// _info.ini says against this game (InfoVersion), or null.
    /// </summary>
    internal static void Judge(PluginEntry p, Result r, string matching, bool strict, string declared)
    {
        string why = KeptOutBecause(r, declared, strict);
        if (why != null)
        {
            p.Stop(PluginState.NotStarted, why);
            return;
        }
        if (declared != null)
            p.Warnings.Add(declared);
        p.MissingUses.AddRange(r.Uses);
        if (r.Uses.Count > 0 && matching != null && r.Lookups.Count == 0)
            p.OtherMapBuildOf = matching;           // expected: a note in the report, no warning (Keel reports #13, #16)
        else if (r.Uses.Count > 0)
            p.Warnings.Add($"Uses {r.Uses.Count} thing(s) this game build lacks, e.g. {Report.Short(r.Uses)}. Fine if the mod checks the game version itself; " +
                           "otherwise expect errors when that part runs.");
        if (r.Lookups.Count > 0)
            p.Warnings.Add($"Looks up {r.Lookups.Count} type(s)/method(s) by name that this game build lacks, e.g. {Report.Short(r.Lookups)} (Harmony patch targets, AccessTools and reflection lookups). " +
                           "That code cannot find its target here: expect errors from the mod, or a part of it that does nothing. (Keel does not check whether a transpiler still matches.)");
    }

    /// <summary>Why the check keeps a DLL out (GameVersionCheck Standard or Strict), or null when it may start.</summary>
    internal static string KeptOutBecause(Result r, string declared, bool strict)
    {
        bool nothingMissing = r.ClassLoad.Count == 0 && r.Uses.Count == 0 && r.Lookups.Count == 0;
        if (declared != null && strict && nothingMissing)
            return $"Made for another game version. {declared} (GameVersionCheck = Strict.)";
        if (r.ClassLoad.Count > 0)
            return $"Made for another game version: this build has no {Report.Short(r.ClassLoad)}. Loading it would stop missions from loading, so Keel leaves it out.";
        if (r.Fatal.Count > 0)
            return $"A patch of this mod does not fit the game and would read or write the wrong memory: {Report.Short(r.Fatal)}. That can crash the game, so Keel leaves it out.";
        if (!nothingMissing && strict)
            return $"Made for another game version: this build has no {Report.Short(r.Uses.Concat(r.Lookups).ToList())} (GameVersionCheck = Strict).";
        return null;
    }

    private readonly HashSet<string> gameAssemblies;
    private readonly DefaultAssemblyResolver resolver = new();
    private readonly Dictionary<string, bool> exists = new();
    private readonly Dictionary<string, Result> byDll = new(StringComparer.OrdinalIgnoreCase);

    public Preflight(string managedDir, string bepInExCore)
    {
        // The game's assemblies = what is in Managed, minus the runtime itself (the same on every build).
        gameAssemblies = new HashSet<string>(
            Directory.GetFiles(managedDir, "*.dll").Select(Path.GetFileNameWithoutExtension)
                     .Where(n => !(n == "mscorlib" || n == "netstandard" || n == "System" || n.StartsWith("System.") || n.StartsWith("Mono."))),
            StringComparer.OrdinalIgnoreCase);
        this.managedDir = managedDir;
        resolver.AddSearchDirectory(managedDir);
        resolver.AddSearchDirectory(bepInExCore);
    }

    public Result Check(string dll, byte[] image = null)
    {
        if (image == null && byDll.TryGetValue(dll, out Result cached))
            return cached;
        var r = new Result();
        using var stream = new MemoryStream(image ?? File.ReadAllBytes(dll), writable: false);
        using ModuleDefinition mod = ModuleDefinition.ReadModule(stream, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });
        var classLoad = new SortedSet<string>();
        var uses = new SortedSet<string>();
        foreach (TypeDefinition type in mod.GetTypes())
        {
            Missing(classLoad, type.BaseType);
            foreach (InterfaceImplementation i in type.Interfaces)
                Missing(classLoad, i.InterfaceType);
            foreach (FieldDefinition f in type.Fields)
                Missing(classLoad, f.FieldType);
            foreach (MethodDefinition method in type.Methods)
                ScanMethod(method, uses);
        }
        var patches = new SortedSet<string>();
        try
        {
            CheckPatches(mod, patches, r);
        }
        catch (Exception)
        {
            r.PatchCheckIncomplete = true;
        }
        r.ClassLoad.AddRange(classLoad);
        r.Uses.AddRange(uses.Except(classLoad));
        r.Lookups.AddRange(patches);
        return r;
    }

    // A type, method or field of the game's own assemblies (not of the mod, the runtime or BepInEx).
    private bool Game(TypeReference t) =>
        t?.GetElementType().Scope is AssemblyNameReference a && gameAssemblies.Contains(a.Name);

    private bool Exists(MemberReference m)
    {
        string key = m.FullName;
        if (exists.TryGetValue(key, out bool ok))
            return ok;
        try
        {
            ok = m switch
            {
                TypeReference t => t.Resolve() != null,
                MethodReference x => x.Resolve() != null,
                FieldReference f => f.Resolve() != null,
                _ => true,
            };
        }
        catch (Exception)
        {
            ok = false;
        }
        exists[key] = ok;
        return ok;
    }

    // A game type this build lacks, also inside generic arguments, arrays and pointers.
    private void Missing(SortedSet<string> into, TypeReference t)
    {
        switch (t)
        {
            case null:
            case GenericParameter:
                return;
            case GenericInstanceType g:
                Missing(into, g.ElementType);
                foreach (TypeReference a in g.GenericArguments)
                    Missing(into, a);
                return;
            case TypeSpecification s:
                Missing(into, s.ElementType);
                return;
        }
        if (Game(t) && !Exists(t))
            into.Add(t.FullName);
    }

    private void ScanMethod(MethodDefinition method, SortedSet<string> uses)
    {
        Missing(uses, method.ReturnType);
        foreach (ParameterDefinition p in method.Parameters)
            Missing(uses, p.ParameterType);
        if (!method.HasBody)
            return;
        foreach (VariableDefinition v in method.Body.Variables)
            Missing(uses, v.VariableType);
        foreach (Instruction ins in method.Body.Instructions)
        {
            switch (ins.Operand)
            {
                case MethodReference m:
                    MethodReference element = m is GenericInstanceMethod gm ? gm.ElementMethod : m;
                    Missing(uses, element.DeclaringType);
                    if (Game(element.DeclaringType) && !Exists(element))
                        uses.Add($"{element.DeclaringType.GetElementType().FullName}::{element.Name}({string.Join(", ", element.Parameters.Select(p => p.ParameterType.Name))})");
                    break;
                case FieldReference f:
                    Missing(uses, f.DeclaringType);
                    if (Game(f.DeclaringType) && !Exists(f))
                        uses.Add($"{f.DeclaringType.GetElementType().FullName}::{f.Name}");
                    break;
                case TypeReference t:
                    Missing(uses, t);
                    break;
            }
        }
    }

    // ---- Targets named by string: Harmony patches and reflection lookups ------------------------------------------------
    // The IL scan above only sees what the code references. A Harmony patch or a reflection lookup names its target as a string,
    // so a method or type that this build renamed or removed passes it and fails when the code runs. Checked here:
    //  - [HarmonyPatch(typeof(X), "Name", ...)] (class and method level merged the way Harmony does, also ("Ns.X", "Name"));
    //  - AccessTools.Method/DeclaredMethod/Field/DeclaredField/PropertyGetter/PropertySetter with typeof(X) and a literal name,
    //    with "Ns.X:Name", or with a Type that came from a literal name (the usual way to reach an internal class);
    //  - AccessTools.TypeByName / Type.GetType / Assembly.GetType with a literal name.
    // Not checked: names built at run time, TargetMethod(), and whether a transpiler's code still matches the target's IL.

    /// <summary>HarmonyLib.MethodType, by its numbers (Keel does not reference Harmony's enum in the attribute data it reads).</summary>
    private enum MethodKind { Normal = 0, Getter = 1, Setter = 2, Constructor = 3, StaticConstructor = 4, Enumerator = 5, Async = 6 }

    private sealed class PatchSpec
    {
        public TypeReference Type;
        public string TypeName;
        public string Name;
        public List<TypeReference> Args;
        public MethodKind Kind;

        public PatchSpec Over(PatchSpec outer) => new()
        {
            Type = Type ?? outer.Type,
            TypeName = TypeName ?? outer.TypeName,
            Name = Name ?? outer.Name,
            Args = Args ?? outer.Args,
            Kind = Kind != MethodKind.Normal ? Kind : outer.Kind,
        };

        /// <summary>The name of the method Harmony patches (null when none is named).</summary>
        public string MethodName => Kind switch
        {
            MethodKind.Getter => "get_" + Name,
            MethodKind.Setter => "set_" + Name,
            MethodKind.Constructor => ".ctor",
            MethodKind.StaticConstructor => ".cctor",
            _ => Name,
        };

        /// <summary>The target's methods of that name, narrowed by the argument list when the patch gives one.</summary>
        public IEnumerable<MethodDefinition> Candidates(TypeDefinition target)
        {
            IEnumerable<MethodDefinition> found = target.Methods.Where(m => m.Name == MethodName);
            if (Args == null)
                return found;
            var wanted = Args.Select(a => a?.FullName).ToList();
            return found.Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(wanted));
        }
    }

    private static readonly HashSet<string> PatchMethodAttributes = new() { "HarmonyPrefix", "HarmonyPostfix", "HarmonyTranspiler", "HarmonyFinalizer", "HarmonyReversePatch" };
    private static readonly HashSet<string> PatchMethodNames = new() { "Prefix", "Postfix", "Transpiler", "Finalizer" };

    private static PatchSpec ReadSpec(IEnumerable<CustomAttribute> attributes)
    {
        PatchSpec spec = null;
        foreach (CustomAttribute a in attributes.Where(x => x.AttributeType.Name == "HarmonyPatch" && x.AttributeType.Namespace == "HarmonyLib"))
        {
            spec ??= new PatchSpec();
            var strings = new List<string>();
            foreach (CustomAttributeArgument arg in a.ConstructorArguments)
            {
                switch (arg.Value)
                {
                    case TypeReference t:
                        spec.Type = t;
                        break;
                    case string s:
                        strings.Add(s);
                        break;
                    case CustomAttributeArgument[] list when arg.Type.GetElementType().Name == "Type":
                        spec.Args = list.Select(x => x.Value as TypeReference).ToList();
                        break;
                    case CustomAttributeArgument[] modes when arg.Type.GetElementType().FullName == "HarmonyLib.ArgumentType"
                            && modes.Length == spec.Args?.Count:
                        for (int i = 0; i < modes.Length; i++)
                        {
                            TypeReference type = spec.Args[i];
                            if (type != null)
                                spec.Args[i] = (HarmonyLib.ArgumentType)modes[i].Value switch
                                {
                                    HarmonyLib.ArgumentType.Ref or HarmonyLib.ArgumentType.Out => new ByReferenceType(type),
                                    HarmonyLib.ArgumentType.Pointer => new PointerType(type),
                                    _ => type,
                                };
                        }
                        break;
                    case int kind when arg.Type.Name == "MethodType":
                        spec.Kind = (MethodKind)kind;
                        break;
                }
            }
            if (strings.Count >= 2)
            {
                spec.TypeName = strings[0];
                spec.Name = strings[1];
            }
            else if (strings.Count == 1)
            {
                spec.Name = strings[0];
            }
        }
        return spec;
    }

    private void CheckPatches(ModuleDefinition mod, SortedSet<string> into, Result result)
    {
        foreach (TypeDefinition type in mod.GetTypes())
        {
            PatchSpec onClass = ReadSpec(type.CustomAttributes);
            foreach (MethodDefinition method in type.Methods)
            {
                PatchSpec onMethod = ReadSpec(method.CustomAttributes);
                bool isPatch = onMethod != null
                    || (onClass != null && (PatchMethodNames.Contains(method.Name) || method.CustomAttributes.Any(a => PatchMethodAttributes.Contains(a.AttributeType.Name))));
                if (!isPatch)
                    continue;
                PatchSpec spec = (onMethod ?? new PatchSpec()).Over(onClass ?? new PatchSpec());
                string problem = MissingTarget(spec);
                if (problem != null)
                    into.Add(problem);
                else if (Target(spec) is TypeDefinition target && spec.MethodName != null)
                {
                    MethodDefinition[] candidates = spec.Candidates(target).Take(2).ToArray();
                    if (candidates.Length != 1)
                        continue;
                    MethodDefinition original = candidates[0];
                    RememberTarget(result.PatchTargets, PatchedMethod(original, spec.Kind));
                    if (spec.Kind is not (MethodKind.StaticConstructor or MethodKind.Enumerator or MethodKind.Async))
                        CheckSignature(method, target, original, result.Fatal);
                }
            }
        }
        // Twice: the first pass learns which static fields hold a Type found by name, the second reports.
        var fieldTypes = new Dictionary<string, string>();
        for (int pass = 0; pass < 2; pass++)
            foreach (TypeDefinition type in mod.GetTypes())
                foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
                    new LookupScan(this, method, pass == 1 ? into : new SortedSet<string>(), fieldTypes,
                        pass == 1 ? result.LookupTargets : null).Run();
    }

    // ---- Do a patch's parameters fit the method it patches? ----
    // Harmony passes values by position and name without comparing their types. A patch that takes an int where the game has a float,
    // or a struct where the game has an object, reads or writes the wrong memory, and that is a crash Keel cannot catch. Only sure cases
    // count: "object" takes anything, and an enum may be taken as its underlying number (the usual way to reach a private enum).

    private static TypeReference Plain(TypeReference t) => t is ByReferenceType r ? r.ElementType : t;

    private static TypeDefinition Definition(TypeReference t)
    {
        try
        {
            return t is GenericParameter || t.IsGenericInstance || t.IsArray ? null : t.Resolve();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Fits(TypeReference patch, TypeReference game)
    {
        patch = Plain(patch);
        game = Plain(game);
        if (patch.FullName == game.FullName || patch.FullName == "System.Object" || patch is GenericParameter || game is GenericParameter)
            return true;
        TypeDefinition a = Definition(patch), b = Definition(game);
        if (a == null || b == null || !(a.IsValueType || b.IsValueType))
            return true;   // references to objects (the runtime checks those), or something Keel cannot judge
        if (a.IsEnum != b.IsEnum && (a.IsEnum ? b : a).IsPrimitive)
            return true;
        return false;
    }

    private TypeDefinition Target(PatchSpec spec) =>
        spec.Type != null ? (Game(spec.Type) ? ResolveType(spec.Type) : null) : spec.TypeName != null ? FindType(spec.TypeName) : null;

    private static void RememberTarget(HashSet<string> targets, MethodDefinition method)
    {
        if (targets != null && method?.Module.Assembly?.Name.Name == MethodSnapshot.AssemblyName)
            targets.Add(MethodSnapshot.Id(method));
    }

    private static MethodDefinition PatchedMethod(MethodDefinition original, MethodKind kind)
    {
        if (kind is not (MethodKind.Enumerator or MethodKind.Async))
            return original;
        string attribute = kind == MethodKind.Enumerator ? "IteratorStateMachineAttribute" : "AsyncStateMachineAttribute";
        CustomAttribute marker = original.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices." + attribute);
        if (marker?.ConstructorArguments.FirstOrDefault().Value is TypeReference machine)
            return ResolveType(machine)?.Methods.FirstOrDefault(m => m.Name == "MoveNext" && !m.HasParameters);
        return null;
    }

    private static void CheckSignature(MethodDefinition patch, TypeDefinition type, MethodDefinition target, List<string> fatal)
    {
        foreach (ParameterDefinition p in patch.Parameters)
        {
            TypeReference other = Injected(p.Name, type, target);
            if (other != null && !Fits(p.ParameterType, other))
                fatal.Add($"{patch.DeclaringType.Name}.{patch.Name} takes '{p.Name}' as {Plain(p.ParameterType).Name}, but {type.Name}.{target.Name} has {Plain(other).Name}");
        }
    }

    // What Harmony hands a patch parameter of this name: the result, a field (___name), an argument by index (__0) or by name.
    private static TypeReference Injected(string name, TypeDefinition type, MethodDefinition target)
    {
        if (name == "__result")
            return target.ReturnType.FullName == "System.Void" ? null : target.ReturnType;
        if (name.StartsWith("___"))
        {
            string field = name.Substring(3);
            for (TypeDefinition t = type; t != null; t = Base(t))
                if (t.Fields.FirstOrDefault(f => f.Name == field)?.FieldType is TypeReference found)
                    return found;
            return null;
        }
        if (name.StartsWith("__"))
            return int.TryParse(name.Substring(2), out int index) && index >= 0 && index < target.Parameters.Count ? target.Parameters[index].ParameterType : null;
        return target.Parameters.FirstOrDefault(x => x.Name == name)?.ParameterType;
    }

    private string MissingTarget(PatchSpec spec)
    {
        string typeName = spec.Type != null ? spec.Type.GetElementType().FullName : spec.TypeName;
        if (typeName == null || (spec.Type != null && !Game(spec.Type)))
            return null;
        TypeDefinition target = Target(spec);
        if (target == null)
            return spec.Type != null || IsGameName(typeName) ? typeName : null;
        if (spec.Kind == MethodKind.StaticConstructor)
            return target.Methods.Any(m => m.Name == ".cctor") ? null : $"{typeName}::.cctor";
        if (spec.MethodName == null || (spec.Kind == MethodKind.Enumerator
            ? target.Methods.Any(m => m.Name == spec.MethodName) : spec.Candidates(target).Any()))
            return null;   // no method named (TargetMethod() or an incomplete attribute): nothing to compare with this build
        string args = spec.Args != null ? string.Join(", ", spec.Args.Select(a => a?.Name)) : "";
        return $"{typeName}::{spec.MethodName}({args})";
    }

    private static TypeDefinition ResolveType(TypeReference type)
    {
        try
        {
            return type.GetElementType().Resolve();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TypeDefinition Base(TypeDefinition t)
    {
        try
        {
            return t.BaseType?.Resolve();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- Index of the game's type names, built the first time a mod looks a type up by name ----------------------------

    private Dictionary<string, TypeDefinition> typeIndex;
    private HashSet<string> gameRoots;
    private readonly List<ModuleDefinition> indexed = new();
    private readonly string managedDir;
    /// <summary>Milliseconds spent building the type index (0 until a mod needed it).</summary>
    public long IndexMs { get; private set; }

    private void EnsureIndex()
    {
        if (typeIndex != null)
            return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        typeIndex = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        gameRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in gameAssemblies)
        {
            try
            {
                ModuleDefinition m = ModuleDefinition.ReadModule(Path.Combine(managedDir, name + ".dll"),
                    new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred });
                indexed.Add(m);
                foreach (TypeDefinition t in m.GetTypes())
                {
                    typeIndex.TryAdd(t.FullName.Replace('/', '+'), t);
                    if (t.Namespace.Length > 0)
                        gameRoots.Add(t.Namespace.Split('.')[0]);
                }
            }
            catch (Exception)
            {
                // A file Cecil cannot read just stays out of the index.
            }
        }
        gameRoots.ExceptWith(new[] { "System", "Microsoft", "Mono" });
        IndexMs = watch.ElapsedMilliseconds;
    }

    private static string WithoutAssembly(string name)
    {
        int comma = name.IndexOf(',');
        return comma >= 0 ? name.Substring(0, comma) : name;
    }

    /// <summary>A type of the game by its full name ("Ns.Type", nested "Ns.Outer+Inner"; ", Assembly" is ignored).</summary>
    private TypeDefinition FindType(string name)
    {
        EnsureIndex();
        return typeIndex.TryGetValue(WithoutAssembly(name).Trim(), out TypeDefinition t) ? t : null;
    }

    /// <summary>Does this name look like one of the game's own namespaces? (Names outside them are not ours to judge.)</summary>
    private bool IsGameName(string name)
    {
        EnsureIndex();
        name = WithoutAssembly(name);
        int dot = name.IndexOf('.');
        return dot > 0 && gameRoots.Contains(name.Substring(0, dot));
    }

    /// <summary>A type named by a string: its definition, or whether the name is the game's and this build lacks it.</summary>
    private (TypeDefinition def, bool missing) Named(string name)
    {
        TypeDefinition def = FindType(name);
        return (def, def == null && IsGameName(name));
    }

    public void Dispose()
    {
        foreach (ModuleDefinition m in indexed)
            m.Dispose();
        resolver.Dispose();
    }

    /// <summary>
    /// One method's lookups by name: types found by a literal name (and the locals and static fields that keep them), and
    /// AccessTools member lookups on such a type, on typeof(X) or on "Ns.X:Name".
    /// </summary>
    private sealed class LookupScan
    {
        private readonly Preflight owner;
        private readonly MethodDefinition method;
        private readonly Mono.Collections.Generic.Collection<Instruction> ins;
        private readonly SortedSet<string> into;
        private readonly Dictionary<string, string> fieldTypes;                                 // static fields holding a Type found by a literal name
        private readonly HashSet<string> targets;
        private readonly Dictionary<VariableDefinition, string> locals = new();                // locals holding one

        public LookupScan(Preflight owner, MethodDefinition method, SortedSet<string> into, Dictionary<string, string> fieldTypes, HashSet<string> targets)
        {
            this.owner = owner;
            this.method = method;
            ins = method.Body.Instructions;
            this.into = into;
            this.fieldTypes = fieldTypes;
            this.targets = targets;
        }

        public void Run()
        {
            for (int i = 0; i < ins.Count; i++)
            {
                if (ins[i].Operand is not MethodReference call)
                    continue;
                string byName = TypeNameCall(call, ins, i);
                if (byName != null)
                    TypeByName(byName, i);
                else if (call.DeclaringType.FullName == "HarmonyLib.AccessTools")
                    MemberLookup(call, i);
            }
        }

        private void TypeByName(string name, int i)
        {
            if (owner.Named(name).missing)
                into.Add(name);
            if (i + 1 >= ins.Count)
                return;
            Instruction next = ins[i + 1];
            if (IsStloc(next) && LocalOf(next, method.Body) is VariableDefinition local)
                locals[local] = name;
            else if (next.OpCode.Code == Code.Stsfld && next.Operand is FieldReference f)
                fieldTypes[f.FullName] = name;
        }

        private static readonly HashSet<string> Shapes = new() { "Method", "Field", "PropertyGetter", "PropertySetter" };

        private void MemberLookup(MethodReference call, int i)
        {
            bool declaredOnly = call.Name.StartsWith("Declared");
            string shape = declaredOnly ? call.Name.Substring("Declared".Length) : call.Name;
            if (!Shapes.Contains(shape) || call.Parameters.Count == 0)
                return;
            // The name and where the type comes from: the nearest literal string before the call.
            int s = NearestLdstr(i, 8);
            if (s < 0)
                return;
            (string typeName, TypeDefinition def, bool missing, string member) = TypeOf(call.Parameters[0].ParameterType.FullName, (string)ins[s].Operand, s);
            if (typeName == null)
                return;
            if (def == null)
            {
                if (missing)
                    into.Add(typeName);
                return;
            }
            string memberName = shape == "PropertyGetter" ? "get_" + member : shape == "PropertySetter" ? "set_" + member : member;
            bool found = false;
            for (TypeDefinition t = def; t != null && !found; t = declaredOnly ? null : Base(t))
            {
                if (shape == "Field")
                    found = t.Fields.Any(f => f.Name == member);
                else
                {
                    MethodDefinition[] candidates = t.Methods.Where(m => m.Name == memberName).Take(2).ToArray();
                    found = candidates.Length != 0;
                    if (candidates.Length == 1)
                        RememberTarget(targets, candidates[0]);
                }
            }
            if (!found)
                into.Add($"{typeName}::{memberName}");
        }

        private int NearestLdstr(int i, int within)
        {
            for (int j = i - 1; j >= 0 && j >= i - within; j--)
                if (ins[j].OpCode.Code == Code.Ldstr)
                    return j;
            return -1;
        }

        // The type a lookup works on and the member it names: from "Ns.X:Name", or from what put the Type on the stack before
        // the name (typeof, a local or a static field holding a by-name Type, or a by-name call). typeName null: not known here,
        // left alone rather than guessed.
        private (string typeName, TypeDefinition def, bool missing, string member) TypeOf(string firstParameter, string literal, int s)
        {
            if (firstParameter == "System.String")
            {
                int colon = literal.IndexOf(':');
                if (colon <= 0)
                    return default;
                string name = literal.Substring(0, colon);
                (TypeDefinition def, bool missing) = owner.Named(name);
                return (name, def, missing, literal.Substring(colon + 1));
            }
            if (firstParameter != "System.Type" || s < 1)
                return default;
            Instruction before = ins[s - 1];
            if (before.Operand is MethodReference g && g.Name == "GetTypeFromHandle" && s >= 2
                && ins[s - 2].OpCode.Code == Code.Ldtoken && ins[s - 2].Operand is TypeReference t)
            {
                if (!owner.Game(t))
                    return default;
                TypeDefinition def = ResolveType(t);
                return (t.GetElementType().FullName, def, def == null, literal);
            }
            string viaName = StoredTypeName(before, s);
            if (viaName == null)
                return default;
            (TypeDefinition named, bool lacks) = owner.Named(viaName);
            return (viaName, named, lacks, literal);
        }

        // A by-name Type kept in a local, returned by a by-name call (or its dup), or kept in a static field.
        private string StoredTypeName(Instruction before, int s)
        {
            if (IsLdloc(before) && LocalOf(before, method.Body) is VariableDefinition local && locals.TryGetValue(local, out string viaLocal))
                return viaLocal;
            if (TypeNameBefore(ins, s - 1) is string viaCall)
                return viaCall;
            if (before.OpCode.Code == Code.Ldsfld && before.Operand is FieldReference sf && fieldTypes.TryGetValue(sf.FullName, out string viaField))
                return viaField;
            return null;
        }
    }

    // The literal name a call takes a type by: AccessTools.TypeByName, Type.GetType, Assembly.GetType.
    private static string TypeNameCall(MethodReference call, Mono.Collections.Generic.Collection<Instruction> ins, int i)
    {
        bool byName = (call.DeclaringType.FullName == "HarmonyLib.AccessTools" && call.Name == "TypeByName")
                   || (call.DeclaringType.FullName == "System.Type" && call.Name == "GetType")
                   || (call.DeclaringType.FullName == "System.Reflection.Assembly" && call.Name == "GetType");
        if (!byName || call.Parameters.Count == 0 || call.Parameters[0].ParameterType.FullName != "System.String")
            return null;
        for (int j = i - 1; j >= 0 && j >= i - 3; j--)
            if (ins[j].OpCode.Code == Code.Ldstr)
                return (string)ins[j].Operand;
        return null;
    }

    // The literal name of the type the instruction at k leaves on the stack, when that is a by-name call (or its dup).
    private static string TypeNameBefore(Mono.Collections.Generic.Collection<Instruction> ins, int k)
    {
        if (k >= 1 && ins[k].OpCode.Code == Code.Dup)
            k--;
        return k >= 0 && ins[k].Operand is MethodReference m ? TypeNameCall(m, ins, k) : null;
    }

    // Cecil gives the variable as the operand of ldloc/ldloc.s/stloc/stloc.s but not of the short forms ldloc.0 .. stloc.3.
    private static VariableDefinition LocalOf(Instruction ins, MethodBody body)
    {
        int index = ins.OpCode.Code switch
        {
            Code.Ldloc_0 or Code.Stloc_0 => 0,
            Code.Ldloc_1 or Code.Stloc_1 => 1,
            Code.Ldloc_2 or Code.Stloc_2 => 2,
            Code.Ldloc_3 or Code.Stloc_3 => 3,
            _ => -1,
        };
        if (index < 0)
            return ins.Operand as VariableDefinition;
        return body.Variables.Count > index ? body.Variables[index] : null;
    }

    private static bool IsStloc(Instruction ins) =>
        ins.OpCode.Code is Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3;

    private static bool IsLdloc(Instruction ins) =>
        ins.OpCode.Code is Code.Ldloc or Code.Ldloc_S or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3;
}
