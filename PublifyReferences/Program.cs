using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PublifyReferences
{
    internal class Program
    {
        private static readonly Type compilerGeneratedType = typeof(CompilerGeneratedAttribute);
        private static readonly AssemblyResolver resolver = new AssemblyResolver();

        private static void CreatePublicAssembly(string source, string target)
        {
            var assembly = AssemblyDefinition.ReadAssembly(source,
                new ReaderParameters { AssemblyResolver = resolver });

            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.GetTypes())
                {
                    if (!type.IsNested)
                    {
                        type.IsPublic = true;
                    }
                    else
                    {
                        type.IsPublic = true;
                        type.IsNestedPublic = true;
                    }

                    foreach (var field in type.Fields)
                    {
                        if (!type.Properties.Any(property => property.Name == field.Name) && !type.Events.Any(e => e.Name == field.Name))
                            field.IsPublic = true;
                    }

                    foreach (var method in type.Methods)
                    {
                        if (/*UseEmptyMethodBodies && */method.HasBody)
                        {
                            var emptyBody = new Mono.Cecil.Cil.MethodBody(method);
                            emptyBody.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldnull));
                            emptyBody.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Throw));
                            method.Body = emptyBody;
                        }

                        method.IsPublic = true;
                    }
                }
            }

            assembly.Write(target);
        }

        private static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: PublifyReferences.exe srcAssemblyPath outputPath");
                Console.WriteLine("srcAssemblyPath can be a file or folder.");
                Console.WriteLine("outputPath has to be a folder.");
                return;
            }

            if (Directory.Exists(args[1]))
            {
                IEnumerable<string> files = Array.Empty<string>();

                if (File.Exists(args[0]))
                {
                    files = new[] { args[0] };

                    var srcDir = Path.GetDirectoryName(args[0]);
                    resolver.AddSearchDirectory(srcDir);
                    foreach (var dir in Directory.EnumerateDirectories(srcDir, "*", new EnumerationOptions() { RecurseSubdirectories = true }))
                        resolver.AddSearchDirectory(dir);
                }
                else if (Directory.Exists(args[0]))
                {
                    files = Directory.EnumerateFiles(args[0], "*.dll")
                        .Concat(Directory.EnumerateFiles(args[0], "*.exe"));

                    resolver.AddSearchDirectory(args[0]);
                    foreach (var dir in Directory.EnumerateDirectories(args[0], "*", new EnumerationOptions() { RecurseSubdirectories = true }))
                        resolver.AddSearchDirectory(dir);
                }

                foreach (var file in files)
                    try
                    {
                        CreatePublicAssembly(file, Path.Combine(args[1], Path.GetFileName(file)));
                        Console.WriteLine("Publified " + Path.GetFileName(file));
                    }
                    catch (BadImageFormatException)
                    {
                        Console.WriteLine("Failed to load " + file);
                    }
            }
        }

        private class AssemblyResolver : IAssemblyResolver
        {
            private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void AddSearchDirectory(string directory)
            {
                _directories.Add(directory);
            }

            public void Dispose()
            {
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters());
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                var assembly = SearchDirectory(name, _directories, parameters);
                if (assembly != null)
                {
                    return assembly;
                }

                throw new AssemblyResolutionException(name);
            }

            private AssemblyDefinition GetAssembly(string file, ReaderParameters parameters)
            {
                if (parameters.AssemblyResolver == null)
                    parameters.AssemblyResolver = this;

                return ModuleDefinition.ReadModule(file, parameters).Assembly;
            }

            private AssemblyDefinition SearchDirectory(AssemblyNameReference name, IEnumerable<string> directories, ReaderParameters parameters)
            {
                var extensions = name.IsWindowsRuntime ? new[] { ".winmd", ".dll" } : new[] { ".exe", ".dll" };
                foreach (var directory in directories)
                {
                    foreach (var extension in extensions)
                    {
                        var file = Path.Combine(directory, name.Name + extension);
                        if (!File.Exists(file))
                            continue;
                        try
                        {
                            return GetAssembly(file, parameters);
                        }
                        catch (BadImageFormatException)
                        {
                        }
                    }
                }

                return null;
            }
        }
    }
}