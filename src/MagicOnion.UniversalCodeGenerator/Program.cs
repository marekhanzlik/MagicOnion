﻿using MagicOnion.CodeAnalysis;
using MagicOnion.Generator;
using MicroBatchFramework;
using Microsoft.CodeAnalysis;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MagicOnion.CodeGenerator
{
    public class Program : BatchBase
    {
        static async Task Main(string[] args)
        {
            await BatchHost.CreateDefaultBuilder().RunBatchEngineAsync<Program>(args);
        }

        public void Run(
            [Option("i", "Input path of analyze csproj.")]string input,
            [Option("o", "Output path(file) or directory base(in separated mode).")]string output,
            [Option("u", "Unuse UnityEngine's RuntimeInitializeOnLoadMethodAttribute on MagicOnionInitializer.")]bool unuseUnityAttr = false,
            [Option("n", "Conditional compiler symbol.")]string @namespace = "MagicOnion",
            [Option("c", "Set namespace root name.")]string[] conditionalSymbol = null)
        {
            // Prepare args
            conditionalSymbol = conditionalSymbol ?? new string[0];

            // Generator Start...

            var sw = Stopwatch.StartNew();
            Console.WriteLine("Project Compilation Start:" + input);

            var collector = new MethodCollector(input, conditionalSymbol);

            Console.WriteLine("Project Compilation Complete:" + sw.Elapsed.ToString());
            Console.WriteLine();

            sw.Restart();
            Console.WriteLine("Method Collect Start");

            var definitions = collector.CollectServiceInterface();
            var hubDefinitions = collector.CollectHubInterface();

            GenericSerializationInfo[] genericInfos;
            EnumSerializationInfo[] enumInfos;
            ExtractResolverInfo(definitions, out genericInfos, out enumInfos);
            ExtractResolverInfo(hubDefinitions.Select(x => x.hubDefinition).ToArray(), out var genericInfos2, out var enumInfos2);
            ExtractResolverInfo(hubDefinitions.Select(x => x.receiverDefintion).ToArray(), out var genericInfos3, out var enumInfos3);
            enumInfos = enumInfos.Concat(enumInfos2).Concat(enumInfos3).Distinct().ToArray();
            genericInfos = genericInfos.Concat(genericInfos2).Concat(genericInfos3).Distinct().ToArray();

            Console.WriteLine("Method Collect Complete:" + sw.Elapsed.ToString());

            Console.WriteLine("Output Generation Start");
            sw.Restart();

            var enumTemplates = enumInfos.GroupBy(x => x.Namespace)
                .OrderBy(x => x.Key)
                .Select(x => new EnumTemplate()
                {
                    Namespace = @namespace + ".Formatters",
                    enumSerializationInfos = x.ToArray()
                })
                .ToArray();

            var resolverTemplate = new ResolverTemplate()
            {
                Namespace = @namespace + ".Resolvers",
                FormatterNamespace = @namespace + ".Formatters",
                ResolverName = "MagicOnionResolver",
                registerInfos = genericInfos.OrderBy(x => x.FullName).Cast<IResolverRegisterInfo>().Concat(enumInfos.OrderBy(x => x.FullName)).ToArray()
            };

            var texts = definitions
                .GroupBy(x => x.Namespace)
                .OrderBy(x => x.Key)
                .Select(x => new CodeTemplate()
                {
                    Namespace = x.Key,
                    Interfaces = x.ToArray()
                })
                .ToArray();

            var hubTexts = hubDefinitions
                .GroupBy(x => x.hubDefinition.Namespace)
                .OrderBy(x => x.Key)
                .Select(x => new HubTemplate()
                {
                    Namespace = x.Key,
                    Interfaces = x.ToArray()
                })
                .ToArray();

            var registerTemplate = new RegisterTemplate
            {
                Namespace = @namespace,
                Interfaces = definitions.Where(x => x.IsServiceDifinition).ToArray(),
                HubInterfaces = hubDefinitions,
                UnuseUnityAttribute = unuseUnityAttr
            };

            var sb = new StringBuilder();
            sb.AppendLine(registerTemplate.TransformText());
            sb.AppendLine(resolverTemplate.TransformText());
            foreach (var item in enumTemplates)
            {
                sb.AppendLine(item.TransformText());
            }

            foreach (var item in texts)
            {
                sb.AppendLine(item.TransformText());
            }

            foreach (var item in hubTexts)
            {
                sb.AppendLine(item.TransformText());
            }

            Output(output, sb.ToString());

            Console.WriteLine("String Generation Complete:" + sw.Elapsed.ToString());
            Console.WriteLine();
        }

        static void Output(string path, string text)
        {
            path = path.Replace("global::", "");

            const string prefix = "[Out]";
            Console.WriteLine(prefix + path);

            var fi = new FileInfo(path);
            if (!fi.Directory.Exists)
            {
                fi.Directory.Create();
            }

            System.IO.File.WriteAllText(path, text, Encoding.UTF8);
        }

        static readonly SymbolDisplayFormat binaryWriteFormat = new SymbolDisplayFormat(
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly);

        static readonly HashSet<string> embeddedTypes = new HashSet<string>(new string[]
        {
            "short",
            "int",
            "long",
            "ushort",
            "uint",
            "ulong",
            "float",
            "double",
            "bool",
            "byte",
            "sbyte",
            "decimal",
            "char",
            "string",
            "System.Guid",
            "System.TimeSpan",
            "System.DateTime",
            "System.DateTimeOffset",

            "MessagePack.Nil",

            // and arrays
            
            "short[]",
            "int[]",
            "long[]",
            "ushort[]",
            "uint[]",
            "ulong[]",
            "float[]",
            "double[]",
            "bool[]",
            "byte[]",
            "sbyte[]",
            "decimal[]",
            "char[]",
            "string[]",
            "System.DateTime[]",
            "System.ArraySegment<byte>",
            "System.ArraySegment<byte>?",

            // extensions

            "UnityEngine.Vector2",
            "UnityEngine.Vector3",
            "UnityEngine.Vector4",
            "UnityEngine.Quaternion",
            "UnityEngine.Color",
            "UnityEngine.Bounds",
            "UnityEngine.Rect",

            "System.Reactive.Unit",
        });

        static readonly Dictionary<string, string> additionalSupportGenericFormatter = new Dictionary<string, string>
        {
            {"System.Collections.Generic.List<>", "global::MessagePack.Formatters.ListFormatter<TREPLACE>()" },
            {"System.Collections.Generic.Dictionary<,>", "global::MessagePack.Formatters.DictionaryFormatter<TREPLACE>()"},
        };

        static void ExtractResolverInfo(InterfaceDefinition[] definitions, out GenericSerializationInfo[] genericInfoResults, out EnumSerializationInfo[] enumInfoResults)
        {
            var genericInfos = new List<GenericSerializationInfo>();
            var enumInfos = new List<EnumSerializationInfo>();

            foreach (var method in definitions.SelectMany(x => x.Methods))
            {
                var namedResponse = method.UnwrappedOriginalResposneTypeSymbol as INamedTypeSymbol;

                if (method.UnwrappedOriginalResposneTypeSymbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Array)
                {
                    var array = method.UnwrappedOriginalResposneTypeSymbol as IArrayTypeSymbol;
                    if (!embeddedTypes.Contains(array.ToString()))
                    {
                        MakeArray(array, genericInfos);
                        if (array.ElementType.TypeKind == TypeKind.Enum)
                        {
                            MakeEnum(array.ElementType as INamedTypeSymbol, enumInfos);
                        }
                    }
                }
                else if (method.UnwrappedOriginalResposneTypeSymbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
                {
                    var enumType = method.UnwrappedOriginalResposneTypeSymbol as INamedTypeSymbol;
                    MakeEnum(enumType, enumInfos);
                }
                else if (namedResponse != null && namedResponse.IsGenericType)
                {
                    // generic type handling
                    var genericType = namedResponse.ConstructUnboundGenericType();
                    var genericTypeString = genericType.ToDisplayString();
                    var fullName = namedResponse.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    string formatterString;

                    if (genericTypeString == "T?")
                    {
                        var more = namedResponse.TypeArguments[0];
                        if (more.TypeKind == TypeKind.Enum)
                        {
                            MakeEnum(more as INamedTypeSymbol, enumInfos);
                        }

                        MakeNullable(namedResponse, genericInfos);
                    }
                    else if (additionalSupportGenericFormatter.TryGetValue(genericTypeString, out formatterString))
                    {
                        MakeGeneric(namedResponse, formatterString, genericInfos);
                    }
                }

                // paramter type
                foreach (var p in method.Parameters)
                {
                    namedResponse = p.OriginalSymbol.Type as INamedTypeSymbol;

                    if (p.OriginalSymbol.Type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Array)
                    {
                        var array = p.OriginalSymbol.Type as IArrayTypeSymbol;
                        if (embeddedTypes.Contains(array.ToString())) continue;
                        MakeArray(array, genericInfos);
                        if (array.ElementType.TypeKind == TypeKind.Enum)
                        {
                            MakeEnum(array.ElementType as INamedTypeSymbol, enumInfos);
                        }
                    }
                    else if (p.OriginalSymbol.Type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
                    {
                        var enumType = p.OriginalSymbol.Type as INamedTypeSymbol;
                        MakeEnum(enumType, enumInfos);
                    }
                    else if (namedResponse != null && namedResponse.IsGenericType)
                    {
                        // generic type handling
                        var genericType = namedResponse.ConstructUnboundGenericType();
                        var genericTypeString = genericType.ToDisplayString();
                        var fullName = namedResponse.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        string formatterString;

                        if (genericTypeString == "T?")
                        {
                            var more = namedResponse.TypeArguments[0];
                            if (more.TypeKind == TypeKind.Enum)
                            {
                                MakeEnum(more as INamedTypeSymbol, enumInfos);
                            }

                            MakeNullable(namedResponse, genericInfos);
                        }
                        else if (additionalSupportGenericFormatter.TryGetValue(genericTypeString, out formatterString))
                        {
                            MakeGeneric(namedResponse, formatterString, genericInfos);
                        }
                    }
                }

                if (method.Parameters.Length > 1)
                {
                    // create dynamicargumenttuple
                    var parameterArguments = method.Parameters.Select(x => x.OriginalSymbol)
                       .Select(x => $"default({x.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})")
                       .ToArray();

                    var typeArguments = method.Parameters.Select(x => x.OriginalSymbol).Select(x => x.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                    var tupleInfo = new GenericSerializationInfo
                    {
                        FormatterName = $"global::MagicOnion.DynamicArgumentTupleFormatter<{string.Join(", ", typeArguments)}>({string.Join(", ", parameterArguments)})",
                        FullName = $"global::MagicOnion.DynamicArgumentTuple<{string.Join(", ", typeArguments)}>",
                    };
                    genericInfos.Add(tupleInfo);
                }
            }

            genericInfoResults = genericInfos.Distinct().ToArray();
            enumInfoResults = enumInfos.Distinct().ToArray();
        }

        static void MakeArray(IArrayTypeSymbol array, List<GenericSerializationInfo> list)
        {
            var arrayInfo = new GenericSerializationInfo
            {
                FormatterName = $"global::MessagePack.Formatters.ArrayFormatter<{array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>()",
                FullName = array.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            };
            list.Add(arrayInfo);
        }

        static void MakeEnum(INamedTypeSymbol enumType, List<EnumSerializationInfo> list)
        {
            var enumInfo = new EnumSerializationInfo
            {
                Name = enumType.Name,
                Namespace = enumType.ContainingNamespace.IsGlobalNamespace ? null : enumType.ContainingNamespace.ToDisplayString(),
                FullName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                UnderlyingType = enumType.EnumUnderlyingType.ToDisplayString(binaryWriteFormat)
            };
            list.Add(enumInfo);
        }

        static void MakeNullable(INamedTypeSymbol type, List<GenericSerializationInfo> list)
        {
            var info = new GenericSerializationInfo
            {
                FormatterName = $"global::MessagePack.Formatters.NullableFormatter<{type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>()",
                FullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            };
            list.Add(info);
        }

        static void MakeGeneric(INamedTypeSymbol type, string formatterTemplate, List<GenericSerializationInfo> list)
        {
            var typeArgs = string.Join(", ", type.TypeArguments.Select(x => x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            var f = formatterTemplate.Replace("TREPLACE", typeArgs);

            var info = new GenericSerializationInfo
            {
                FormatterName = f,
                FullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            };
            list.Add(info);
        }
    }
}
