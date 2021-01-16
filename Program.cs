using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace blog_CompilingWithRoslyn
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            string code = "using System; namespace InMemoryApp {class Program{private static void Main(string[] args){ foreach (string arg in args) {Console.WriteLine(arg);} } } }";

            var syntaxTree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.CSharp8));
            string basePath = Path.GetDirectoryName(typeof(System.Runtime.GCSettings).GetTypeInfo().Assembly.Location);

            var referencesPaths = new List<string> {
                    typeof(object).GetTypeInfo().Assembly.Location,
                    typeof(Console).GetTypeInfo().Assembly.Location,
                    Path.Combine(basePath, "System.Runtime.dll"),
                    Path.Combine(basePath, "System.Runtime.Extensions.dll"),
                    Path.Combine(basePath, "mscorlib.dll")
                };

            var executableReferences = new List<PortableExecutableReference>();

            foreach (var reference in referencesPaths)
            {
                if (File.Exists(reference))
                {
                    executableReferences.Add(MetadataReference.CreateFromFile(reference));
                }
            }

            var compilation = CSharpCompilation.Create(Path.GetRandomFileName(), new[] { syntaxTree }, executableReferences, new CSharpCompilationOptions(OutputKind.ConsoleApplication));

            using (var memoryStream = new MemoryStream())
            {
                EmitResult compilationResult = compilation.Emit(memoryStream);

                if (!compilationResult.Success)
                {
                    var errors = compilationResult.Diagnostics.Where(diagnostic =>
                       diagnostic.IsWarningAsError ||
                       diagnostic.Severity == DiagnosticSeverity.Error)?.ToList() ?? new List<Diagnostic>();

                    foreach (var error in errors)
                    {
                        Console.WriteLine(error.GetMessage());
                    }
                }
                else
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);

                    AssemblyLoadContext assemblyContext = new AssemblyLoadContext(Path.GetRandomFileName(), true);
                    Assembly assembly = assemblyContext.LoadFromStream(memoryStream);

                    var entryPoint = compilation.GetEntryPoint(CancellationToken.None);
                    var type = assembly.GetType($"{entryPoint.ContainingNamespace.MetadataName}.{entryPoint.ContainingType.MetadataName}");
                    var instance = assembly.CreateInstance(type.FullName);
                    var method = type.GetMethod(entryPoint.MetadataName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    method.Invoke(instance, BindingFlags.InvokeMethod, Type.DefaultBinder, new object[] { new string[] { "abc" } }, null);

                    assemblyContext.Unload();
                }
            }
        }
    }
}