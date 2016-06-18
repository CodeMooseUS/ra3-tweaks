using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RA3Tweaks.Installer
{
    public class Installer
    {
        public static void Main(string[] args)
        {
            // Set the defaults
            string gamePath = @"%ProgramFiles%\Steam\steamapps\common\Robot Arena 3";
            string tweakSrcPath = @".\ra3-tweaks.dll";
            string assetSrcPath = @".\bundles";
            bool isRestore = false;

            // Parse the command line options
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "-?":
                        case "/?":
                            Installer.ShowHelp();
                            return;

                        case "-i":
                        case "/i":
                            gamePath = args[++i];
                            break;

                        case "-t":
                        case "/t":
                            tweakSrcPath = args[++i];
                            break;

                        case "-a":
                        case "/a":
                            assetSrcPath = args[++i];
                            break;

                        case "-r":
                        case "/r":
                            isRestore = true;
                            break;
                    }
                }

            }
            catch (Exception)
            {
                Installer.ShowHelp();
                return;
            }

            // Begin processing
            try
            {
                // Find the C# assembly
                string rootPath = Path.Combine(gamePath, @"RobotArena3_Data\Managed\");
                string assemblyPath = Path.Combine(rootPath, "Assembly-CSharp.dll");
                if (!File.Exists(assemblyPath))
                {
                    // Not found
                    throw new FileNotFoundException("Could not find RA3 assembly @ " + assemblyPath);
                }

                // Find the dll we use to compare versions
                string testPath = Path.Combine(rootPath, "Assembly-CSharp-firstpass.dll");
                if (!File.Exists(testPath))
                {
                    // Not found
                    throw new FileNotFoundException("Could not find firstpass @ " + testPath);
                }

                Console.WriteLine("Found RA3 Assembly @ " + assemblyPath);
                Console.WriteLine("");

                // Find the backup assembly
                string backupPath = Path.Combine(rootPath, "Assembly-CSharp-Backup.dll");
                bool isBackupFound = File.Exists(backupPath);
                bool isGameUpdated = false;
                if (isBackupFound)
                {
                    // Compare file version info
                    if (File.GetLastWriteTimeUtc(testPath) > File.GetLastWriteTimeUtc(backupPath))
                    {
                        // The game was updated after the backup was made
                        isGameUpdated = true;
                    }
                }

                if (isRestore)
                {
                    // Restore the backup
                    if (isBackupFound)
                    {
                        if (isGameUpdated)
                        {
                            Console.WriteLine("Backup found, but was older than the game version (steam update?), so not restoring.");
                            File.Copy(backupPath, assemblyPath, true);
                        }
                        else
                        {
                            Console.WriteLine("Backup found, restoring...");
                            File.Copy(backupPath, assemblyPath, true);
                        }
                        return;
                    }
                    else
                    {
                        throw new FileNotFoundException("Could not find RA3 backup assembly @ " + backupPath);
                    }
                }
                else
                {
                    if (isGameUpdated)
                    {
                        Console.WriteLine("Previous backup found but it was older than the game version (steam update?), deleting old backup...");
                        File.Delete(backupPath);
                        isBackupFound = false;
                    }

                    // Make a backup if it doesn't exist already
                    if (!isBackupFound)
                    {
                        Console.WriteLine("No backup found, creating...");
                        File.Copy(assemblyPath, backupPath);
                        Console.WriteLine("Backup created @ " + backupPath);
                        Console.WriteLine("");
                    }

                    // Find the tweak assembly
                    if (!File.Exists(tweakSrcPath))
                    {
                        // Not found
                        throw new FileNotFoundException("Could not find tweak assembly @ " + tweakSrcPath);
                    }

                    string tweakDllName = Path.GetFileName(tweakSrcPath);
                    string finalPath = Path.Combine(rootPath, tweakDllName);
                    File.Copy(tweakSrcPath, finalPath, true);

                    // Make the asset path
                    if (!Directory.Exists(assetSrcPath))
                    {
                        // Not found
                        throw new FileNotFoundException("Could not find assets @ " + assetSrcPath);
                    }
                    string assetDestPath = Path.Combine(rootPath, @"ra3-tweaks\");
                    if (!Directory.Exists(assetDestPath))
                    {
                        Directory.CreateDirectory(assetDestPath);
                    }

                    // Copy assets to path
                    foreach (var file in Directory.GetFiles(assetSrcPath))
                    {
                        File.Copy(file, Path.Combine(assetDestPath, Path.GetFileName(file)), true);
                    }

                    // Perform the injection
                    Console.WriteLine("Modifying assembly...");
                    Installer.PerformInjection(rootPath, assemblyPath, backupPath, finalPath);
                    Console.WriteLine("Done.");
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine("Error - " + ex.Message);
            }
        }

        private static void PerformInjection(string rootPath, string assemblyPath, string backupPath, string tweakPath)
        {
            // Get the assembly we want to modify
            DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(rootPath);
            AssemblyDefinition ra3 = AssemblyDefinition.ReadAssembly(backupPath, new ReaderParameters { AssemblyResolver = resolver });
            AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(tweakPath, new ReaderParameters { AssemblyResolver = resolver });

            // Read through tweak file and find all the attributes
            Assembly tweakAssembly = Assembly.LoadFrom(tweakPath);

            var methods = mod.MainModule
                .Types
                .SelectMany(t => t.Methods)
                .Where(m => m.IsPublic && m.IsStatic && m.CustomAttributes.Count > 0 && (m.CustomAttributes.Where(a => a.AttributeType.FullName == "RA3Tweaks.TweakAttribute").Count() > 0))
                .ToArray();

            // Modify the call for each method attribute
            for (int i = 0; i < methods.Length; i++)
            {
                var attribs = methods[i].CustomAttributes.Where(a => a.AttributeType.FullName == "RA3Tweaks.TweakAttribute").ToArray();
                for (int j = 0; j < attribs.Length; j++)
                {
                    string fromClassName = attribs[j].ConstructorArguments[0].Value.ToString();
                    string fromMethodName = attribs[j].ConstructorArguments[1].Value.ToString();
                    bool insertAtStart = true;
                    if (attribs[j].ConstructorArguments.Count > 2)
                    {
                        insertAtStart = ((bool)attribs[j].ConstructorArguments[2].Value);
                    }

                    Console.WriteLine(string.Format("Inserting call from {0}.{1} to {2}.{3}...", fromClassName, fromMethodName, methods[i].DeclaringType.Name, methods[i].Name));

                    MethodDefinition callTo = methods[i];
                    MethodDefinition callFrom = ra3.MainModule
                        .Types
                        .Where(t => t.FullName == fromClassName)
                        .SelectMany(t => t.Methods)
                        .Where(m => m.Name == fromMethodName)
                        .First();

                    ILProcessor processor = callFrom.Body.GetILProcessor();
                    Instruction entryInstr = (insertAtStart ? processor.Body.Instructions[0] : processor.Body.Instructions.Last());

                    // Validate parameters
                    bool isValid = (callFrom.Parameters.Count + 1 == callTo.Parameters.Count) && 
                                   (callTo.Parameters[0].ParameterType.FullName == callFrom.DeclaringType.FullName) &&
                                   ((callTo.ReturnType.FullName == "System.Void") ||
                                    (callTo.ReturnType.FullName == "RA3Tweaks.TweakReturnVoid" && callFrom.ReturnType.FullName == "System.Void") ||
                                    callTo.ReturnType.FullName.StartsWith("RA3Tweaks.TweakReturn") && callTo.ReturnType.IsGenericInstance && (callTo.ReturnType as GenericInstanceType).GenericArguments[0].FullName == callFrom.ReturnType.FullName);
                    if (isValid)
                    {
                        for (int k = 0; k < callFrom.Parameters.Count; k++)
                        {
                            if (callFrom.Parameters[k].ParameterType.FullName != callTo.Parameters[k + 1].ParameterType.FullName)
                            {
                                Console.WriteLine("Error - Parameter types do not match.");
                                isValid = false;
                                break;
                            }
                        }
                    }

                    if (!isValid)
                    {
                        Console.WriteLine("Error - Could not validate tweak method, check parameters and return type match.");
                        continue;
                    }

                    // Add parameters to the call
                    processor.InsertBefore(entryInstr, processor.Create(callFrom.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0));
                    if (callFrom.Parameters.Count > 0)
                    {
                        processor.InsertBefore(entryInstr, processor.Create(callFrom.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1));
                    }
                    if (callFrom.Parameters.Count > 1)
                    {
                        processor.InsertBefore(entryInstr, processor.Create(callFrom.IsStatic ? OpCodes.Ldarg_1 : OpCodes.Ldarg_2));
                    }
                    if (callFrom.Parameters.Count > 2)
                    {
                        processor.InsertBefore(entryInstr, processor.Create(callFrom.IsStatic ? OpCodes.Ldarg_2 : OpCodes.Ldarg_3));
                    }
                    if (callFrom.Parameters.Count > 3)
                    {
                        if (callFrom.IsStatic)
                        {
                            processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ldarg_3));
                        }
                        else
                        {
                            processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ldarg_S, callFrom.Parameters[4]));
                        }
                    }
                    if (callFrom.Parameters.Count > 4)
                    {
                        for (int l = 4; l < callFrom.Parameters.Count; l++)
                        {
                            processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ldarg_S, callFrom.Parameters[callFrom.IsStatic ? l - 1 : l]));
                        }
                    }

                    // Add call to hook
                    processor.InsertBefore(entryInstr, processor.Create(OpCodes.Call, ra3.MainModule.Import(callTo.Resolve())));

                    // Check the return value
                    if (methods[i].ReturnType.FullName != "System.Void")
                    {
                        // Create the local variable for the return value
                        var tweakReturnType = callTo.ReturnType;
                        VariableDefinition tweakReturnVar = new VariableDefinition("returnValue", ra3.MainModule.Import(tweakReturnType));
                        processor.Body.Variables.Add(tweakReturnVar);

                        FieldDefinition preventDefaultType = tweakReturnType.Resolve().Fields.First(f => f.Name == "PreventDefault");
                        FieldReference preventDefault = preventDefaultType.Resolve();
                        FieldDefinition returnValueType = null;
                        FieldReference returnValue = null;

                        // If we are looking to return a value from the caller, we create the types here
                        if (tweakReturnType.Resolve().HasGenericParameters)
                        {
                            preventDefault = new FieldReference(preventDefaultType.Name, preventDefaultType.FieldType, tweakReturnType);

                            returnValueType = tweakReturnType.Resolve().Fields.First(f => f.Name == "ReturnValue");
                            returnValue = new FieldReference(returnValueType.Name, returnValueType.FieldType, tweakReturnType);
                        }

                        // Add the check for the 'preventDefault' value
                        processor.InsertBefore(entryInstr, processor.Create(OpCodes.Stloc_S, tweakReturnVar));
                        processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ldloca_S, tweakReturnVar));
                        processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ldfld, ra3.MainModule.Import(preventDefault)));
                        processor.InsertBefore(entryInstr, processor.Create(OpCodes.Brfalse, entryInstr));

                        // Add the actual return value if needed
                        if (callFrom.ReturnType.FullName != "System.Void")
                        {
                            processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ldloca_S, tweakReturnVar));
                            processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ldfld, ra3.MainModule.Import(returnValue)));
                        }

                        processor.InsertBefore(entryInstr, processor.Create(OpCodes.Ret));
                    }
                }
            }

            File.Delete(assemblyPath);
            ra3.Write(assemblyPath);
        }

        private static void ShowHelp()
        {
            Console.WriteLine("");
            Console.WriteLine("Usage: ra3-tweaker.exe <options>");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("-?                 Display this help message");
            Console.WriteLine("-i <path>          Full path to the RA3 install directory");
            Console.WriteLine("-t <path>          Full path to tweak dll");
            Console.WriteLine("-a <path>          Full path to the asset bundles");
            Console.WriteLine("-r                 Restore the modified file back to the original RA3 state");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("ra3-tweaker.exe");
            Console.WriteLine("ra3-tweaker.exe -i \"D:\\Games\\Steam\\steamapps\\common\\Robot Arena 3\" -t \"C:\\Folder\\to\\ra3-tweaks.dll\" -a \"D:\\path\\ra3-tweaks\\bundles\"");
            Console.WriteLine("ra3-tweaker.exe -r");
            Console.WriteLine("");
        }
    }
}