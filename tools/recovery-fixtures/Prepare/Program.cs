using Mono.Cecil;

if (args.Length != 2) throw new ArgumentException("Pass the built fixture DLL and a separate output DLL path.");
using var module = ModuleDefinition.ReadModule(args[0]);
var game = new AssemblyNameReference("Seapower-Scripts", new Version(0, 0, 0, 0));
module.AssemblyReferences.Add(game);
var missing = new TypeReference("SeaPower", "KeelRecoveryGone", module, game);
var method = module.GetType("KeelRecovery.FailureCode").Methods.Single(m => m.Name == "MissingMetadata");
foreach (var instruction in method.Body.Instructions)
{
    if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "KeelRecovery.ReferenceStub")
        instruction.Operand = new MethodReference(call.Name, module.TypeSystem.Void, missing);
    else if (instruction.Operand is TypeReference type && type.FullName == "KeelRecovery.ReferenceStub")
        instruction.Operand = missing;
}
module.Write(args[1]);
Console.WriteLine("Prepared absent game API references in " + args[1]);
