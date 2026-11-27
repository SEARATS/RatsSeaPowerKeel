using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SeaPowerKeel;

// The release tool and the loader use the same format and hashing rules.
internal class MethodSnapshot
{
    internal const string DirectoryName = "method-history-v1";
    internal const string Extension = ".kh.gz";
    internal const string AssemblyName = "Seapower-Scripts";
    internal const string AssemblyFile = AssemblyName + ".dll";
    private const int HashSize = 32;
    private const int MaxMethods = 250_000;
    private const int MaxEdges = 100_000;
    internal class Fingerprint(byte[] body, byte[] callers, byte[] callees, int[] calls)
    {
        public byte[] Body { get; } = body;
        public byte[] Callers { get; } = callers;
        public byte[] Callees { get; } = callees;
        public int[] Calls { get; } = calls;
    }

    public string Build { get; }
    public string AssemblyHash { get; }
    public IReadOnlyDictionary<string, Fingerprint> Methods { get; }

    private MethodSnapshot(string build, string assemblyHash, Dictionary<string, Fingerprint> methods)
    {
        Build = build;
        AssemblyHash = assemblyHash;
        Methods = methods;
    }

    public static string Id(MethodReference method)
    {
        method = method.GetElementMethod();
        return method.FullName + (method.HasGenericParameters ? "`" + method.GenericParameters.Count : "");
    }

    public static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream));
    }

    public static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

    public static MethodSnapshot Scan(string build, byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using ModuleDefinition module = ModuleDefinition.ReadModule(stream);
        using var sha = SHA256.Create();
        MethodDefinition[] methods = module.GetTypes().SelectMany(t => t.Methods).OrderBy(Id, StringComparer.Ordinal).ToArray();
        string[] ids = methods.Select(Id).ToArray();
        var index = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var bodies = new byte[methods.Length][];
        var calls = new int[methods.Length][];
        var incoming = methods.Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < methods.Length; i++)
        {
            MethodDefinition method = methods[i];
            bodies[i] = Hash(sha, w => WriteBody(w, method));
            var targets = new SortedSet<int>();
            if (method.HasBody)
                foreach (Instruction instruction in method.Body.Instructions)
                    if (instruction.Operand is MethodReference target && Scope(target.DeclaringType) == module.Assembly.Name.Name
                        && index.TryGetValue(Id(LocalDefinition(target)), out int j))
                        targets.Add(j);
            calls[i] = targets.ToArray();
            foreach (int j in calls[i])
                incoming[j].Add(i);
        }
        int[][] callers = incoming.Select(v => v.ToArray()).ToArray();
        byte[][] callerHashes = GraphHashes(sha, ids, bodies, callers, calls);
        byte[][] calleeHashes = GraphHashes(sha, ids, bodies, calls, callers);
        var entries = new Dictionary<string, Fingerprint>(methods.Length, StringComparer.Ordinal);
        for (int i = 0; i < methods.Length; i++)
            entries.Add(ids[i], new Fingerprint(bodies[i], callerHashes[i], calleeHashes[i], calls[i]));
        return new MethodSnapshot(build, Hex(sha.ComputeHash(image)), entries);
    }

    private static MethodReference LocalDefinition(MethodReference target) =>
        target.DeclaringType.IsGenericInstance ? target.Resolve() ?? target : target;

    private static string Scope(TypeReference type) => type.GetElementType().Scope switch
    {
        AssemblyNameReference assembly => assembly.Name,
        ModuleDefinition module => module.Assembly.Name.Name,
        IMetadataScope scope => scope.Name,
        _ => "",
    };

    private static void WriteBody(BinaryWriter writer, MethodDefinition method)
    {
        writer.Write((int)method.Attributes);
        writer.Write((int)method.ImplAttributes);
        writer.Write((int)method.CallingConvention);
        writer.Write(TypeId(method.ReturnType));
        writer.Write(method.Parameters.Count);
        foreach (ParameterDefinition parameter in method.Parameters)
        {
            writer.Write(parameter.Name ?? "");
            writer.Write(TypeId(parameter.ParameterType));
            writer.Write((int)parameter.Attributes);
            writer.Write(parameter.HasConstant);
            if (parameter.HasConstant)
                writer.Write(Convert.ToString(parameter.Constant, CultureInfo.InvariantCulture) ?? "");
        }
        writer.Write(method.HasBody);
        if (!method.HasBody)
            return;

        MethodBody body = method.Body;
        writer.Write(body.InitLocals);
        writer.Write(body.Variables.Count);
        foreach (VariableDefinition local in body.Variables)
            writer.Write(TypeId(local.VariableType));
        var offsets = body.Instructions.Select((instruction, index) => (instruction, index))
            .ToDictionary(x => x.instruction, x => x.index);
        int Position(Instruction instruction) => instruction == null ? -1 : offsets[instruction];
        writer.Write(body.Instructions.Count);
        foreach (Instruction instruction in body.Instructions)
        {
            writer.Write(instruction.OpCode.Value);
            switch (instruction.Operand)
            {
                case Instruction branch:
                    writer.Write(Position(branch));
                    break;
                case Instruction[] branches:
                    writer.Write(branches.Length);
                    foreach (Instruction target in branches)
                        writer.Write(Position(target));
                    break;
                case VariableDefinition local: writer.Write(local.Index); break;
                case ParameterDefinition parameter: writer.Write(parameter.Index); break;
                case MethodReference target: writer.Write(Scope(target.DeclaringType) + "/" + target.FullName + "/" + Id(target)); break;
                case FieldReference field: writer.Write(Scope(field.DeclaringType) + "/" + field.FullName); break;
                case TypeReference type: writer.Write(TypeId(type)); break;
                case CallSite signature: writer.Write(signature.FullName); break;
                case float value: writer.Write(value); break;
                case double value: writer.Write(value); break;
                case object value: writer.Write(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
            }
        }
        writer.Write(body.ExceptionHandlers.Count);
        foreach (ExceptionHandler handler in body.ExceptionHandlers)
        {
            writer.Write((int)handler.HandlerType);
            writer.Write(handler.CatchType == null ? "" : TypeId(handler.CatchType));
            writer.Write(Position(handler.TryStart));
            writer.Write(Position(handler.TryEnd));
            writer.Write(Position(handler.HandlerStart));
            writer.Write(Position(handler.HandlerEnd));
            writer.Write(Position(handler.FilterStart));
        }
    }

    private static string TypeId(TypeReference type) => Scope(type) + "/" + type.FullName;

    private static byte[] Hash(HashAlgorithm sha, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return sha.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length);
    }

    // Collapse recursion with two iterative DFS passes. Hash each component after its dependencies.
    // Running this in each direction avoids including a caller's unrelated callees (or vice versa).
    private static byte[][] GraphHashes(HashAlgorithm sha, string[] ids, byte[][] bodies, int[][] edges, int[][] reverse)
    {
        var seen = new bool[ids.Length];
        var finish = new List<int>(ids.Length);
        var visit = new Stack<(int Node, int Next)>();
        for (int root = 0; root < ids.Length; root++)
        {
            if (seen[root])
                continue;
            seen[root] = true;
            visit.Push((root, 0));
            while (visit.Count != 0)
            {
                var (node, next) = visit.Pop();
                if (next == edges[node].Length)
                {
                    finish.Add(node);
                    continue;
                }
                visit.Push((node, next + 1));
                int child = edges[node][next];
                if (!seen[child])
                {
                    seen[child] = true;
                    visit.Push((child, 0));
                }
            }
        }

        int[] componentOf = Enumerable.Repeat(-1, ids.Length).ToArray();
        var components = new List<List<int>>();
        var pending = new Stack<int>();
        for (int i = finish.Count - 1; i >= 0; i--)
        {
            int root = finish[i];
            if (componentOf[root] >= 0)
                continue;
            int component = components.Count;
            var members = new List<int>();
            components.Add(members);
            componentOf[root] = component;
            pending.Push(root);
            while (pending.Count != 0)
            {
                int node = pending.Pop();
                members.Add(node);
                foreach (int child in reverse[node])
                    if (componentOf[child] < 0)
                    {
                        componentOf[child] = component;
                        pending.Push(child);
                    }
            }
            members.Sort();
        }

        var hashes = new byte[components.Count][];
        for (int i = components.Count - 1; i >= 0; i--)
        {
            int component = i;
            hashes[i] = Hash(sha, writer =>
            {
                writer.Write(components[component].Count);
                foreach (int node in components[component])
                {
                    writer.Write(ids[node]);
                    writer.Write(bodies[node]);
                    writer.Write(edges[node].Length);
                    foreach (int target in edges[node])
                    {
                        writer.Write(ids[target]);
                        int targetComponent = componentOf[target];
                        if (targetComponent != component)
                            writer.Write(hashes[targetComponent]);
                    }
                }
            });
        }
        return componentOf.Select(component => hashes[component]).ToArray();
    }

    public void Write(string path)
    {
        WriteAtomically(path, writer =>
        {
            writer.Write(DirectoryName);
            writer.Write(Build);
            writer.Write(AssemblyHash);
            writer.Write(Methods.Count);
            foreach (var entry in Methods.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                writer.Write(entry.Key);
                writer.Write(entry.Value.Body);
                writer.Write(entry.Value.Callers);
                writer.Write(entry.Value.Callees);
                writer.Write(entry.Value.Calls.Length);
                foreach (int target in entry.Value.Calls)
                    writer.Write(target);
            }
        });
    }

    public static MethodSnapshot Read(string path)
    {
        using var reader = Open(path);
        if (reader.ReadString() != DirectoryName)
            throw new InvalidDataException("Unknown method history format.");
        string build = reader.ReadString(), sha = reader.ReadString();
        int count = Count(reader, MaxMethods);
        var entries = new Dictionary<string, Fingerprint>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string id = reader.ReadString();
            byte[] body = ReadHash(reader), callers = ReadHash(reader), callees = ReadHash(reader);
            var calls = new int[Count(reader, MaxEdges)];
            for (int j = 0; j < calls.Length; j++)
                calls[j] = Count(reader, count - 1);
            entries.Add(id, new Fingerprint(body, callers, callees, calls));
        }
        return new MethodSnapshot(build, sha, entries);
    }

    private static byte[] ReadHash(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(HashSize);
        return bytes.Length == HashSize ? bytes : throw new EndOfStreamException();
    }

    internal static int Count(BinaryReader reader, int max)
    {
        int count = reader.ReadInt32();
        return count >= 0 && count <= max ? count : throw new InvalidDataException("Invalid count in method history.");
    }

    internal static BinaryReader Open(string path) => new(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));

    internal static void WriteAtomically(string path, Action<BinaryWriter> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new BinaryWriter(new GZipStream(File.Create(temporary), CompressionLevel.Optimal)))
                write(writer);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
